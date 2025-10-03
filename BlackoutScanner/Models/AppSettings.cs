using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        private float _ocrConfidenceThreshold = 90.0f;
        private List<string> _selectedLanguages = new List<string> { "eng", "kor", "jpn", "chi_sim", "chi_tra", "rus" };
        private bool _useAIEnhancedOCR = false;
        private ObservableCollection<AIProviderConfiguration> _aiProviders = new ObservableCollection<AIProviderConfiguration>();

        // Legacy fields - kept for migration purposes, marked obsolete
        [Obsolete("Use AIProviders collection instead")]
        private string _aiProvider = "None";
        [Obsolete("Use AIProviders collection instead")]
        private string _nvidiaApiKey = string.Empty;
        [Obsolete("Use AIProviders collection instead")]
        private string _nvidiaModel = "baidu/paddleocr";
        [Obsolete("Use AIProviders collection instead")]
        private string _openAIApiKey = string.Empty;
        [Obsolete("Use AIProviders collection instead")]
        private string _openAIModel = "gpt-4-vision-preview";
        [Obsolete("Use AIProviders collection instead")]
        private string _geminiApiKey = string.Empty;
        [Obsolete("Use AIProviders collection instead")]
        private string _geminiModel = "gemini-pro-vision";
        [Obsolete("Use AIProviders collection instead")]
        private string _customEndpointUrl = string.Empty;
        [Obsolete("Use AIProviders collection instead")]
        private string _customEndpointApiKey = string.Empty;
        [Obsolete("Use AIProviders collection instead")]
        private string _customEndpointModel = string.Empty;

        // UI State Settings
        private bool _debugSettingsExpanded = false;
        private bool _ocrSettingsExpanded = true;
        private bool _aiSettingsExpanded = false;
        private bool _hotkeySettingsExpanded = false;
        private bool _exportSettingsExpanded = false;

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

        public List<string> SelectedLanguages
        {
            get => _selectedLanguages;
            set
            {
                if (_selectedLanguages != value)
                {
                    Serilog.Log.Information($"[AppSettings.SelectedLanguages] BEFORE set - Count: {_selectedLanguages?.Count ?? 0}, Languages: [{string.Join(", ", _selectedLanguages ?? new List<string>())}]");
                    Serilog.Log.Information($"[AppSettings.SelectedLanguages] NEW value - Count: {value?.Count ?? 0}, Languages: [{string.Join(", ", value ?? new List<string>())}]");

                    // Deduplicate to prevent duplicates from ever being stored
                    if (value != null)
                    {
                        var originalCount = value.Count;
                        var deduplicated = value.Distinct().ToList();
                        if (originalCount != deduplicated.Count)
                        {
                            Serilog.Log.Warning($"[AppSettings.SelectedLanguages] Deduplicating languages from {originalCount} to {deduplicated.Count} items");
                            _selectedLanguages = deduplicated;
                        }
                        else
                        {
                            _selectedLanguages = value;
                        }
                    }
                    else
                    {
                        _selectedLanguages = value ?? new List<string>();
                    }

                    Serilog.Log.Information($"[AppSettings.SelectedLanguages] AFTER set - Count: {_selectedLanguages?.Count ?? 0}, Languages: [{string.Join(", ", _selectedLanguages ?? new List<string>())}]");
                    OnPropertyChanged();
                }
            }
        }

        public bool UseAIEnhancedOCR
        {
            get => _useAIEnhancedOCR;
            set
            {
                if (_useAIEnhancedOCR != value)
                {
                    _useAIEnhancedOCR = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Collection of configured AI providers for cascading OCR fallback.
        /// Providers are tried in priority order until confidence threshold is met.
        /// </summary>
        public ObservableCollection<AIProviderConfiguration> AIProviders
        {
            get => _aiProviders;
            set
            {
                if (_aiProviders != value)
                {
                    _aiProviders = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== Legacy Properties (kept for migration support) =====
        // These will be removed in a future version after migration is complete

        [Obsolete("Use AIProviders collection instead")]
        public string AIProvider
        {
            get => _aiProvider;
            set
            {
                if (_aiProvider != value)
                {
                    _aiProvider = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string NvidiaApiKey
        {
            get => _nvidiaApiKey;
            set
            {
                if (_nvidiaApiKey != value)
                {
                    _nvidiaApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string NvidiaModel
        {
            get => _nvidiaModel;
            set
            {
                if (_nvidiaModel != value)
                {
                    _nvidiaModel = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string OpenAIApiKey
        {
            get => _openAIApiKey;
            set
            {
                if (_openAIApiKey != value)
                {
                    _openAIApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string OpenAIModel
        {
            get => _openAIModel;
            set
            {
                if (_openAIModel != value)
                {
                    _openAIModel = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string GeminiApiKey
        {
            get => _geminiApiKey;
            set
            {
                if (_geminiApiKey != value)
                {
                    _geminiApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string GeminiModel
        {
            get => _geminiModel;
            set
            {
                if (_geminiModel != value)
                {
                    _geminiModel = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string CustomEndpointUrl
        {
            get => _customEndpointUrl;
            set
            {
                if (_customEndpointUrl != value)
                {
                    _customEndpointUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string CustomEndpointApiKey
        {
            get => _customEndpointApiKey;
            set
            {
                if (_customEndpointApiKey != value)
                {
                    _customEndpointApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        [Obsolete("Use AIProviders collection instead")]
        public string CustomEndpointModel
        {
            get => _customEndpointModel;
            set
            {
                if (_customEndpointModel != value)
                {
                    _customEndpointModel = value;
                    OnPropertyChanged();
                }
            }
        }

        // UI State Properties
        public bool DebugSettingsExpanded
        {
            get => _debugSettingsExpanded;
            set
            {
                if (_debugSettingsExpanded != value)
                {
                    _debugSettingsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool OCRSettingsExpanded
        {
            get => _ocrSettingsExpanded;
            set
            {
                if (_ocrSettingsExpanded != value)
                {
                    _ocrSettingsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AISettingsExpanded
        {
            get => _aiSettingsExpanded;
            set
            {
                if (_aiSettingsExpanded != value)
                {
                    _aiSettingsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HotkeySettingsExpanded
        {
            get => _hotkeySettingsExpanded;
            set
            {
                if (_hotkeySettingsExpanded != value)
                {
                    _hotkeySettingsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ExportSettingsExpanded
        {
            get => _exportSettingsExpanded;
            set
            {
                if (_exportSettingsExpanded != value)
                {
                    _exportSettingsExpanded = value;
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
