using Microsoft.UI;
using Microsoft.UI.Xaml;
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

        private double _thumbnailImageSize = 80;
        public double ThumbnailImageSize
        {
            get => _thumbnailImageSize;
            private set
            {
                if (_thumbnailImageSize != value)
                {
                    _thumbnailImageSize = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _thumbnailItemWidth = 100;
        public double ThumbnailItemWidth
        {
            get => _thumbnailItemWidth;
            private set
            {
                if (_thumbnailItemWidth != value)
                {
                    _thumbnailItemWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _thumbnailItemHeight = 120;
        public double ThumbnailItemHeight
        {
            get => _thumbnailItemHeight;
            private set
            {
                if (_thumbnailItemHeight != value)
                {
                    _thumbnailItemHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _thumbnailIconSize = 48;
        public double ThumbnailIconSize
        {
            get => _thumbnailIconSize;
            private set
            {
                if (_thumbnailIconSize != value)
                {
                    _thumbnailIconSize = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailTabWidth = 34;
        public double FolderThumbnailTabWidth
        {
            get => _folderThumbnailTabWidth;
            private set
            {
                if (_folderThumbnailTabWidth != value)
                {
                    _folderThumbnailTabWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailTabHeight = 12;
        public double FolderThumbnailTabHeight
        {
            get => _folderThumbnailTabHeight;
            private set
            {
                if (_folderThumbnailTabHeight != value)
                {
                    _folderThumbnailTabHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailBodyWidth = 76;
        public double FolderThumbnailBodyWidth
        {
            get => _folderThumbnailBodyWidth;
            private set
            {
                if (_folderThumbnailBodyWidth != value)
                {
                    _folderThumbnailBodyWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailBodyHeight = 58;
        public double FolderThumbnailBodyHeight
        {
            get => _folderThumbnailBodyHeight;
            private set
            {
                if (_folderThumbnailBodyHeight != value)
                {
                    _folderThumbnailBodyHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailInsetWidth = 58;
        public double FolderThumbnailInsetWidth
        {
            get => _folderThumbnailInsetWidth;
            private set
            {
                if (_folderThumbnailInsetWidth != value)
                {
                    _folderThumbnailInsetWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _folderThumbnailInsetHeight = 38;
        public double FolderThumbnailInsetHeight
        {
            get => _folderThumbnailInsetHeight;
            private set
            {
                if (_folderThumbnailInsetHeight != value)
                {
                    _folderThumbnailInsetHeight = value;
                    OnPropertyChanged();
                }
            }
        }

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
                    OnPropertyChanged(nameof(IconVisibility));
                    OnPropertyChanged(nameof(ImageThumbnailVisibility));
                    OnPropertyChanged(nameof(FolderThumbnailVisibility));
                }
            }
        }

        public Visibility IconVisibility => Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ImageThumbnailVisibility => Thumbnail != null && !IsDirectory ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FolderThumbnailVisibility => Thumbnail != null && IsDirectory && !IsParentDirectory ? Visibility.Visible : Visibility.Collapsed;

        public void ApplyThumbnailSize(double imageSize)
        {
            var clamped = System.Math.Clamp(imageSize, 64, 180);
            ThumbnailImageSize = clamped;
            ThumbnailItemWidth = clamped + 20;
            ThumbnailItemHeight = clamped + 42;
            ThumbnailIconSize = System.Math.Max(36, clamped * 0.6);
            FolderThumbnailTabWidth = clamped * 0.42;
            FolderThumbnailTabHeight = clamped * 0.15;
            FolderThumbnailBodyWidth = clamped * 0.92;
            FolderThumbnailBodyHeight = clamped * 0.70;
            FolderThumbnailInsetWidth = clamped * 0.70;
            FolderThumbnailInsetHeight = clamped * 0.46;
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
