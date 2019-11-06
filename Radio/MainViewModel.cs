using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;

namespace Radio
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName]string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private int volume;
        public int Volume
        {
            get => volume;
            set
            {
                volume = value;
                OnPropertyChanged();
                if (volumeWaveProvider != null)
                {
                    volumeWaveProvider.Volume = volume / 100.0f;
                }
                if (volume > 0)
                {
                    isMuted = false;
                }
            }
        }

        private int channelIndex;
        public int ChannelIndex
        {
            get => channelIndex;
            set
            {
                channelIndex = value;
                channelIndex %= Channels.Count;
                if (channelIndex < 0)
                {
                    channelIndex = Channels.Count - 1;
                }
                OnPropertyChanged();

                Channel = Channels[channelIndex];

                ChannelIndexStr = $"{channelIndex + 1}/{Channels.Count}";
            }
        }

        private string channelIndexStr;
        public string ChannelIndexStr
        {
            get => channelIndexStr;
            set
            {
                channelIndexStr = value;
                OnPropertyChanged();
            }
        }

        private Channel channel;
        public Channel Channel
        {
            get => channel;
            set
            {
                channel = value;
                OnPropertyChanged();

                if (CancellationToken != null)
                {
                    CancellationToken.Cancel();
                    networkThread.Wait();
                }
                BufferLife = 0;
                CancellationToken = new CancellationTokenSource();

                outputDevice = new WaveOutEvent()
                {
                    Volume = 0.5f
                };

                networkThread = Task.Run(() =>
                {
                    try
                    {
                        IMp3FrameDecompressor decompressor = null;
                        WebRequest request = WebRequest.Create(Channel.StreamUrl);
                        using (Stream responseStream = request.GetResponse().GetResponseStream())
                        {
                            var buffer = new byte[1024 * 128]; // needs to be big enough to hold a decompressed frame

                            var readFullyStream = new ReadFullyStream(responseStream);

                            while (!CancellationToken.IsCancellationRequested)
                            {
                                if (IsBufferNearlyFull)
                                {
                                    Debug.WriteLine("Буфер почти заполнен - ждем освобождения");
                                    Thread.Sleep(500);
                                }
                                else
                                {
                                    Mp3Frame frame;
                                    try
                                    {
                                        frame = Mp3Frame.LoadFromStream(readFullyStream);
                                    }
                                    catch (EndOfStreamException)
                                    {
                                        // reached the end of the MP3 file / stream
                                        break;
                                    }
                                    catch (WebException)
                                    {
                                        // probably we have aborted download from the GUI thread
                                        break;
                                    }

                                    if (frame == null)
                                        break;
                                    if (decompressor == null)
                                    {
                                        decompressor = CreateFrameDecompressor(frame);
                                        bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat)
                                        {
                                            BufferDuration = TimeSpan.FromSeconds(15)
                                        };

                                        volumeWaveProvider = new VolumeWaveProvider16(bufferedWaveProvider)
                                        {
                                            Volume = Volume / 100f
                                        };
                                        outputDevice.Init(volumeWaveProvider);
                                        PlaybackState = StreamingPlaybackState.Buffering;
                                    }
                                    int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                                    bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                    finally
                    {
                        Stop();
                    }
                }, CancellationToken.Token);
            }
        }

        private float bufferLife;
        public float BufferLife
        {
            get => bufferLife;
            set
            {
                bufferLife = value;
                OnPropertyChanged();
            }
        }

        public bool IsBufferNearlyFull => bufferedWaveProvider != null &&
                       bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes
                       < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        private StreamingPlaybackState playbackState;
        public StreamingPlaybackState PlaybackState
        {
            get => playbackState;
            set
            {
                playbackState = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int savedVolume;

        private bool isMuted;
        public bool IsMuted
        {
            get => isMuted;
            set
            {
                isMuted = value;
                OnPropertyChanged();
                if (isMuted)
                {
                    savedVolume = Volume;
                    Volume = 0;
                }
                else
                {
                    Volume = savedVolume;
                }
            }
        }

        private MainWindow window;
        private IWavePlayer outputDevice;
        private VolumeWaveProvider16 volumeWaveProvider;
        private BufferedWaveProvider bufferedWaveProvider;
        private CancellationTokenSource CancellationToken;
        private Task networkThread;
        private NotifyIcon notifyIcon;

        public List<Channel> Channels { get; set; }

        public MainViewModel(MainWindow mainWindow)
        {
            window = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            Channels = new List<Channel>() {
                 new Channel("Trancemission", "http://air.radiorecord.ru:8102/tm_320"),
                 new Channel("Russian Mix", "http://air.radiorecord.ru:8102/rus_320"),
                 new Channel("Chill-Ou", "http://air.radiorecord.ru:8102/chil_320 "),
                 new Channel("Club", "http://air.radiorecord.ru:8102/club_320"),
                 new Channel("Dancecore", "http://air.radiorecord.ru:8102/dc_320"),
                 new Channel("Dubstep", "http://air.radiorecord.ru:8102/dub_320"),
                 new Channel("Trap", "http://air.radiorecord.ru:8102/trap_320"),
                 new Channel("Deep", "http://air.radiorecord.ru:8102/deep_320")
            };

            ChannelIndex = 0;

            Volume = 30;

            DispatcherTimer timer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (s, e) =>
            {
                if (bufferedWaveProvider != null && outputDevice != null)
                {
                    var time = bufferedWaveProvider.BufferedDuration.TotalSeconds;
                    BufferLife = (float)time;

                    if (time < 0.5 && PlaybackState == StreamingPlaybackState.Playing)
                    {
                        Pause();
                        PlaybackState = StreamingPlaybackState.Buffering;
                    }
                    else if (time > 2 && PlaybackState == StreamingPlaybackState.Buffering)
                    {
                        Play();
                    }
                }
            };
            timer.Start();

            notifyIcon = new NotifyIcon();
            notifyIcon.Text = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
            notifyIcon.DoubleClick += (s, e) => { window?.Show(); };
            notifyIcon.Icon = new Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream);
            notifyIcon.Visible = true;

            MenuItem[] contextMenuItems = new MenuItem[] {
                new MenuItem("Mute/Unmute", (s,e)=>{
                    IsMuted = !IsMuted;
                }),
                new MenuItem("Next channel", (s,e)=>{
                    ChannelIndex++;
                }),
                new MenuItem("Previous channel", (s,e)=>{
                     ChannelIndex--;
                }),
                new MenuItem("Close", (s,e)=>{
                    window.Close();
                }),
            };
            notifyIcon.ContextMenu = new ContextMenu(contextMenuItems);
        }

        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }

        public void Pause()
        {
            window.pauseButton.Visibility = Visibility.Collapsed;
            window.playButton.Visibility = Visibility.Visible;
            outputDevice?.Pause();
            PlaybackState = StreamingPlaybackState.Paused;
        }

        public void Play()
        {
            window.pauseButton.Visibility = Visibility.Visible;
            window.playButton.Visibility = Visibility.Collapsed;
            outputDevice?.Play();
            PlaybackState = StreamingPlaybackState.Playing;
        }

        public void Stop()
        {
            PlaybackState = StreamingPlaybackState.Stopped;
            outputDevice.Stop();
            outputDevice.Dispose();
            outputDevice = null;
        }

        public void Close()
        {
            CancellationToken.Cancel();
            notifyIcon.Icon.Dispose();
            notifyIcon.Dispose();
            window.Close();
        }

        public ICommand PreviousChannelCommand => new Command(() =>
        {
            ChannelIndex--;
        });

        public ICommand NextChannelCommand => new Command(() =>
        {
            ChannelIndex++;
        });

        public ICommand PauseCommand => new Command(() =>
        {
            Pause();
        });

        public ICommand PlayCommand => new Command(() =>
        {
            Play();
        }, x => PlaybackState != StreamingPlaybackState.Buffering);

        public ICommand MuteCommand => new Command(() =>
        {
            IsMuted = !IsMuted;
        }, x => PlaybackState != StreamingPlaybackState.Buffering);

        public ICommand CloseCommand => new Command(() =>
        {
            Close();
        }, x => PlaybackState != StreamingPlaybackState.Buffering);
    }
}