using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BlackoutScanner.Models
{
    public class AppSettings : INotifyPropertyChanged
    {
        private string _exportFolder = "Exports";
        private bool _saveDebugImages = false;
        private string _logLevel = "Information";
        private string _debugImagesFolder = "DebugImages";
        private string _hotKey = "Ctrl+Q";
        private bool _useLocalTimeInExports = false;
        private bool _useMultiEngineOCR = false;
        private float _ocrConfidenceThreshold = 80.0f;

        public string ExportFolder
        {
            get => _exportFolder;
            set
            {
                if (_exportFolder != value)
                {
                    _exportFolder = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseLocalTimeInExports
        {
            get => _useLocalTimeInExports;
            set
            {
                if (_useLocalTimeInExports != value)
                {
                    _useLocalTimeInExports = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool SaveDebugImages
        {
            get => _saveDebugImages;
            set
            {
                if (_saveDebugImages != value)
                {
                    _saveDebugImages = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LogLevel
        {
            get => _logLevel;
            set
            {
                if (_logLevel != value)
                {
                    _logLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DebugImagesFolder
        {
            get => _debugImagesFolder;
            set
            {
                if (_debugImagesFolder != value)
                {
                    _debugImagesFolder = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HotKey
        {
            get => _hotKey;
            set
            {
                if (_hotKey != value)
                {
                    _hotKey = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool UseMultiEngineOCR
        {
            get => _useMultiEngineOCR;
            set
            {
                if (_useMultiEngineOCR != value)
                {
                    _useMultiEngineOCR = value;
                    OnPropertyChanged();
                }
            }
        }

        public float OCRConfidenceThreshold
        {
            get => _ocrConfidenceThreshold;
            set
            {
                if (_ocrConfidenceThreshold != value)
                {
                    _ocrConfidenceThreshold = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
