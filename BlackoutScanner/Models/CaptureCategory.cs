using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace BlackoutScanner.Models
{
    public class CaptureCategory : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        private RelativeBounds _relativeBounds = new RelativeBounds();
        public RelativeBounds RelativeBounds
        {
            get => _relativeBounds;
            set
            {
                if (_relativeBounds != value)
                {
                    _relativeBounds = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Bounds));
                }
            }
        }

        // For backward compatibility and UI display
        [JsonIgnore]
        public Rectangle Bounds
        {
            get => _boundsCache;
            set
            {
                _boundsCache = value;
                // Note: This requires a container rectangle to properly convert
                // The actual conversion will happen in the context where the container is known
                OnPropertyChanged();
            }
        }
        private Rectangle _boundsCache;

        [JsonIgnore]
        private BitmapImage? _previewImage;
        [JsonIgnore]
        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<CaptureField> Fields { get; set; } = new ObservableCollection<CaptureField>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void NotifyBoundsChanged()
        {
            OnPropertyChanged(nameof(Bounds));
        }
    }
}
