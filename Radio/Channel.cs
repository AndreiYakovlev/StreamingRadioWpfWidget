using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Radio
{
    public class Channel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName]string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private string title;
        public string Title
        {
            get => title;
            set
            {
                title = value;
                OnPropertyChanged();
            }
        }

        private string url;
        public string StreamUrl
        {
            get => url;
            set
            {
                url = value;
                OnPropertyChanged();
            }
        }

        public Channel()
        {
        }

        public Channel(string title, string streamUrl)
        {
            Title = title?.Trim() ?? throw new ArgumentNullException(nameof(title));
            StreamUrl = streamUrl?.Trim() ?? throw new ArgumentNullException(nameof(streamUrl));
        }
    }
}