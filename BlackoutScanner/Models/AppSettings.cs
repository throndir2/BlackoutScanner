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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
