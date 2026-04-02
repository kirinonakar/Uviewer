using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Uviewer.Models
{
    public class ImageProcessingViewModel : INotifyPropertyChanged
    {
        private double _upscaleFactor = 2.0;
        private double _sharpenAmount = 1.0;
        private double _sharpenThreshold = 0.01;
        private double _unsharpAmount = 2.0;
        private double _unsharpRadius = 1.0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double UpscaleFactor
        {
            get => _upscaleFactor;
            set => SetProperty(ref _upscaleFactor, value);
        }

        public double SharpenAmount
        {
            get => _sharpenAmount;
            set => SetProperty(ref _sharpenAmount, value);
        }

        public double SharpenThreshold
        {
            get => _sharpenThreshold;
            set => SetProperty(ref _sharpenThreshold, value);
        }

        public double UnsharpAmount
        {
            get => _unsharpAmount;
            set => SetProperty(ref _unsharpAmount, value);
        }

        public double UnsharpRadius
        {
            get => _unsharpRadius;
            set => SetProperty(ref _unsharpRadius, value);
        }

        public void Reset()
        {
            UpscaleFactor = 2.0;
            SharpenAmount = 1.0;
            SharpenThreshold = 0.01;
            UnsharpAmount = 2.0;
            UnsharpRadius = 1.0;
        }

        public string UpscaleFactorText => $"{UpscaleFactor:F1}x";
        public string SharpenAmountText => $"{SharpenAmount:F1}";
        public string SharpenThresholdText => $"{SharpenThreshold:F3}";
        public string UnsharpAmountText => $"{UnsharpAmount:F1}";
        public string UnsharpRadiusText => $"{UnsharpRadius:F1}";

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            
            // Also notify dependent text properties
            if (propertyName == nameof(UpscaleFactor)) OnPropertyChanged(nameof(UpscaleFactorText));
            if (propertyName == nameof(SharpenAmount)) OnPropertyChanged(nameof(SharpenAmountText));
            if (propertyName == nameof(SharpenThreshold)) OnPropertyChanged(nameof(SharpenThresholdText));
            if (propertyName == nameof(UnsharpAmount)) OnPropertyChanged(nameof(UnsharpAmountText));
            if (propertyName == nameof(UnsharpRadius)) OnPropertyChanged(nameof(UnsharpRadiusText));
            
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
