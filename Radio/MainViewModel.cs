using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLayer;
using NLayer.NAudioSupport;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
                if (outputDevice != null)
                {
                    outputDevice.Volume = volume / 100.0f;
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

                CancellationToken = new CancellationTokenSource();

                outputDevice = new WaveOutEvent()
                {
                    Volume = Volume / 100f,
                };

                networkThread = Task.Run(() =>
                {
                    try
                    {
                        IMp3FrameDecompressor decompressor = null;
                        WebRequest request = WebRequest.Create(Channel.StreamUrl);
                        using (Stream responseStream = request.GetResponse().GetResponseStream())
                        {
                            var buffer = new byte[1024 * 1024]; // needs to be big enough to hold a decompressed frame

                            var readFullyStream = new ReadFullyStream(responseStream);

                            while (!CancellationToken.IsCancellationRequested)
                            {
                                if (IsBufferNearlyFull)
                                {
                                    Debug.WriteLine("Буфер почти заполнен - ждем освобождения");
                                    Thread.Sleep(500);
                                }

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

                                if (frame == null) break;
                                if (decompressor == null)
                                {
                                    decompressor = CreateFrameDecompressor(frame);
                                    bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat)
                                    {
                                        BufferDuration = TimeSpan.FromSeconds(30)
                                    };
                                    outputDevice.Init(bufferedWaveProvider);
                                    PlaybackState = StreamingPlaybackState.Buffering;
                                }
                                int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                                bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                    finally
                    {
                        PlaybackState = StreamingPlaybackState.Stopped;
                        outputDevice.Stop();
                        outputDevice.Dispose();
                        outputDevice = null;
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

        private Window window;
        private IWavePlayer outputDevice;
        private BufferedWaveProvider bufferedWaveProvider;
        private CancellationTokenSource CancellationToken;
        public List<Channel> Channels { get; set; }
        public Task networkThread;

        public MainViewModel(Window mainWindow)
        {
            window = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            Channels = new List<Channel>() {
                 new Channel("Trancemission", "http://air.radiorecord.ru:8102/tm_320"),
                 new Channel("Russian Mix", "http://air.radiorecord.ru:8102/rus_320"),
                 new Channel("Chill-Ou", "http://air.radiorecord.ru:8102/chil_320 "),
                 new Channel("Club", "http://air.radiorecord.ru:8102/club_320"),
                 new Channel("Deep", "http://air.radiorecord.ru:8102/deep_320"),
                 new Channel("Dancecore", "http://air.radiorecord.ru:8102/dc_320"),
                 new Channel("Dubstep", "http://air.radiorecord.ru:8102/dub_320"),
                 new Channel("Trap", "http://air.radiorecord.ru:8102/trap_320"),
                 new Channel("Deep", "http://air.radiorecord.ru:8102/deep_320")
            };

            ChannelIndex = 0;

            Volume = 20;

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
                        outputDevice.Pause();
                        PlaybackState = StreamingPlaybackState.Buffering;
                    }
                    else if (time > 3 && PlaybackState == StreamingPlaybackState.Buffering)
                    {
                        outputDevice.Play();
                    }
                }
            };
            timer.Start();
        }

        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }

        public ICommand PreviousChannelCommand => new Command(() =>
        {
            ChannelIndex--;
        });

        public ICommand NextChannelCommand => new Command(() =>
        {
            ChannelIndex++;
        });
    }
}