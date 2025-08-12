using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace BlackoutScanner.Models
{
    public enum CategoryComparisonMode
    {
        Text,
        Image
    }

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

        private bool _isMultiEntity = false;
        public bool IsMultiEntity
        {
            get => _isMultiEntity;
            set
            {
                if (_isMultiEntity != value)
                {
                    _isMultiEntity = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _entityHeightOffset = 40;
        public int EntityHeightOffset
        {
            get => _entityHeightOffset;
            set
            {
                if (_entityHeightOffset != value)
                {
                    _entityHeightOffset = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _maxEntityCount = 10;
        public int MaxEntityCount
        {
            get => _maxEntityCount;
            set
            {
                if (_maxEntityCount != value)
                {
                    _maxEntityCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private CategoryComparisonMode _comparisonMode = CategoryComparisonMode.Text;
        public CategoryComparisonMode ComparisonMode
        {
            get => _comparisonMode;
            set
            {
                if (_comparisonMode != value)
                {
                    _comparisonMode = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _textToCompare = string.Empty;
        public string TextToCompare
        {
            get => _textToCompare;
            set
            {
                if (_textToCompare != value)
                {
                    _textToCompare = value;
                    OnPropertyChanged();
                }
            }
        }

        private byte[]? _previewImageData;
        public byte[]? PreviewImageData
        {
            get => _previewImageData;
            set
            {
                _previewImageData = value;
                OnPropertyChanged();
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
