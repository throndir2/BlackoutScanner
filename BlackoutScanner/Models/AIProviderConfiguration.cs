using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BlackoutScanner.Models
{
    /// <summary>
    /// Represents the configuration for a single AI OCR provider.
    /// Multiple providers can be configured with different priorities for cascading fallback.
    /// </summary>
    public class AIProviderConfiguration : INotifyPropertyChanged
    {
        private Guid _id;
        private string _providerType;
        private string _displayName;
        private string _model;
        private string _apiKey;
        private int _priority;
        private bool _isEnabled;
        private int _requestsPerMinute;
        private Dictionary<string, string> _additionalSettings;

        /// <summary>
        /// Unique identifier for this provider configuration.
        /// </summary>
        public Guid Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The type of AI provider (e.g., "NvidiaBuild", "Gemini", "OpenAI", "Custom").
        /// </summary>
        public string ProviderType
        {
            get => _providerType;
            set
            {
                if (_providerType != value)
                {
                    _providerType = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// User-friendly display name for this provider configuration.
        /// Example: "NVIDIA PaddleOCR", "Gemini Pro Vision", etc.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// The specific model to use for this provider.
        /// Examples: "baidu/paddleocr", "gemini-1.5-flash", "gpt-4-vision-preview"
        /// </summary>
        public string Model
        {
            get => _model;
            set
            {
                if (_model != value)
                {
                    _model = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// API key or authentication token for this provider.
        /// </summary>
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (_apiKey != value)
                {
                    _apiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Priority for cascade processing. Lower numbers are tried first.
        /// Example: Priority 1 is tried before Priority 2.
        /// </summary>
        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether this provider is enabled and should be used in the cascade.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Maximum requests per minute allowed for this provider.
        /// Used for rate limiting to prevent API quota exhaustion.
        /// Default values: NVIDIA Build = 40, Gemini models vary by tier.
        /// </summary>
        public int RequestsPerMinute
        {
            get => _requestsPerMinute;
            set
            {
                if (_requestsPerMinute != value)
                {
                    _requestsPerMinute = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Additional provider-specific settings as key-value pairs.
        /// Examples: endpoint URL for custom providers, timeout values, etc.
        /// </summary>
        public Dictionary<string, string> AdditionalSettings
        {
            get => _additionalSettings;
            set
            {
                if (_additionalSettings != value)
                {
                    _additionalSettings = value;
                    OnPropertyChanged();
                }
            }
        }

        public AIProviderConfiguration()
        {
            _id = Guid.NewGuid();
            _providerType = string.Empty;
            _displayName = string.Empty;
            _model = string.Empty;
            _apiKey = string.Empty;
            _priority = 1;
            _isEnabled = true;
            _requestsPerMinute = 0; // Will be set based on provider/model
            _additionalSettings = new Dictionary<string, string>();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Creates a deep copy of this configuration.
        /// </summary>
        public AIProviderConfiguration Clone()
        {
            return new AIProviderConfiguration
            {
                Id = this.Id,
                ProviderType = this.ProviderType,
                DisplayName = this.DisplayName,
                Model = this.Model,
                ApiKey = this.ApiKey,
                Priority = this.Priority,
                IsEnabled = this.IsEnabled,
                RequestsPerMinute = this.RequestsPerMinute,
                AdditionalSettings = new Dictionary<string, string>(this.AdditionalSettings)
            };
        }

        public override string ToString()
        {
            return $"{DisplayName} ({ProviderType}/{Model}) - Priority {Priority}" + (IsEnabled ? "" : " [Disabled]");
        }
    }
}
