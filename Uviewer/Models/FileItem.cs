using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Uviewer.Models
{
    // ListView / GridView 바인딩용 파일 항목 클래스
    public class FileItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public bool IsArchive { get; set; }
        public bool IsImage { get; set; }
        public bool IsText { get; set; }
        public bool IsEpub { get; set; }
        public bool IsPdf { get; set; }
        public bool IsParentDirectory { get; set; }
        public bool IsWebDav { get; set; }
        public string? WebDavPath { get; set; }
        public bool IsThumbnailLoading { get; set; }

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Icon => IsParentDirectory ? "\uE72B" :
                              IsDirectory ? "\uE8B7" :
                              IsArchive ? "\uE8D4" :
                              IsEpub ? "\uE82D" :
                              IsPdf ? "\uEA90" :
                              IsImage ? "\uE8B9" :
                              IsText ? "\uE8C4" : "\uE7C3";

        public SolidColorBrush IconColor => IsDirectory || IsParentDirectory ?
            new SolidColorBrush(Colors.Gold) :
            IsArchive ? new SolidColorBrush(Colors.Orange) :
            IsEpub ? new SolidColorBrush(Colors.MediumPurple) :
            IsPdf ? new SolidColorBrush(Colors.IndianRed) :
            IsImage ? new SolidColorBrush(Colors.CornflowerBlue) :
            IsText ? new SolidColorBrush(Colors.LightGreen) :
            new SolidColorBrush(Colors.Gray);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
