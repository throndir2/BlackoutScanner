using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using BlackoutScanner.Utilities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BlackoutScanner.Views
{
    public partial class AIProviderEditorDialog : Window
    {
        private AIProviderConfiguration? _configuration;
        private readonly bool _isEditMode;

        // Model options for each provider type
        private readonly Dictionary<string, List<string>> _providerModels = new Dictionary<string, List<string>>
        {
            { "NvidiaBuild", new List<string> { "baidu/paddleocr", "nvidia/nemoretriever-ocr-v1" } },
            { "Gemini", new List<string> { "gemini-2.5-flash-lite", "gemini-2.5-flash", "gemini-2.5-pro" } },
            //{ "OpenAI", new List<string> { "gpt-4o", "gpt-4-turbo", "gpt-4-vision-preview" } }, // OpenAI not yet supported
            { "Custom", new List<string>() }
        };

        public AIProviderConfiguration? Result => _configuration;

        public AIProviderEditorDialog(AIProviderConfiguration? existingConfig = null)
        {
            InitializeComponent();

            _isEditMode = existingConfig != null;
            _configuration = existingConfig?.Clone() ?? new AIProviderConfiguration();

            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            if (_configuration == null) return;

            // Set provider type
            foreach (ComboBoxItem item in providerTypeComboBox.Items)
            {
                if (item.Tag?.ToString() == _configuration.ProviderType)
                {
                    providerTypeComboBox.SelectedItem = item;
                    break;
                }
            }

            // If no provider type selected yet (new config), default to first option
            if (providerTypeComboBox.SelectedItem == null && providerTypeComboBox.Items.Count > 0)
            {
                providerTypeComboBox.SelectedIndex = 0;
            }

            displayNameTextBox.Text = _configuration.DisplayName;
            priorityTextBox.Text = _configuration.Priority.ToString();
            isEnabledCheckBox.IsChecked = _configuration.IsEnabled;

            // Set API key (PasswordBox doesn't support binding)
            apiKeyPasswordBox.Password = _configuration.ApiKey;

            // Update model dropdown based on provider type
            UpdateModelOptions();

            // Set rate limit (or use default if not set)
            if (_configuration.RequestsPerMinute > 0)
            {
                requestsPerMinuteTextBox.Text = _configuration.RequestsPerMinute.ToString();
            }
            else
            {
                // Set default based on provider/model
                UpdateRateLimitDefault();
            }

            // Set model
            if (!string.IsNullOrEmpty(_configuration.Model))
            {
                // Check if model is in the list
                var modelExists = modelComboBox.Items.Cast<string>().Any(m => m == _configuration.Model);
                if (modelExists)
                {
                    modelComboBox.SelectedItem = _configuration.Model;
                }
                else
                {
                    // Custom model, set as text
                    modelComboBox.Text = _configuration.Model;
                }
            }

            // Load additional settings
            if (_configuration.AdditionalSettings.TryGetValue("EndpointUrl", out var endpointUrl))
            {
                endpointUrlTextBox.Text = endpointUrl;
            }

            UpdateTitle();
        }

        private void UpdateTitle()
        {
            Title = _isEditMode ? "Edit AI Provider" : "Add AI Provider";
        }

        private void ProviderType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateModelOptions();
            UpdateCustomSettingsVisibility();
            UpdateDisplayNameSuggestion();
            UpdateRateLimitDefault();
        }

        private void Model_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRateLimitDefault();
            UpdateDisplayNameSuggestion();
        }

        private void Model_DropDownClosed(object sender, EventArgs e)
        {
            // Update when dropdown closes (user selected from list)
            UpdateRateLimitDefault();
            UpdateDisplayNameSuggestion();
        }

        private void Model_LostFocus(object sender, RoutedEventArgs e)
        {
            // Update when user types and leaves the field
            UpdateRateLimitDefault();
            UpdateDisplayNameSuggestion();
        }

        private void UpdateModelOptions()
        {
            if (providerTypeComboBox.SelectedItem is not ComboBoxItem selectedItem)
                return;

            var providerType = selectedItem.Tag?.ToString() ?? "";
            var currentModel = modelComboBox.Text;

            modelComboBox.Items.Clear();

            if (_providerModels.TryGetValue(providerType, out var models))
            {
                foreach (var model in models)
                {
                    modelComboBox.Items.Add(model);
                }

                // Check if current model is valid for the new provider
                var isCurrentModelValid = models.Contains(currentModel);

                if (modelComboBox.Items.Count > 0)
                {
                    if (isCurrentModelValid && !string.IsNullOrEmpty(currentModel))
                    {
                        // Keep current model if it's valid for this provider
                        modelComboBox.SelectedItem = currentModel;
                    }
                    else
                    {
                        // Select first model as default when switching providers
                        modelComboBox.SelectedIndex = 0;
                    }
                }
            }
        }

        private void UpdateCustomSettingsVisibility()
        {
            if (providerTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var providerType = selectedItem.Tag?.ToString() ?? "";
                customSettingsPanel.Visibility = providerType == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateDisplayNameSuggestion()
        {
            // Only suggest if display name is empty or looks like a previous suggestion
            if (string.IsNullOrWhiteSpace(displayNameTextBox.Text) ||
                displayNameTextBox.Text.Contains("NVIDIA") ||
                displayNameTextBox.Text.Contains("Gemini") ||
                displayNameTextBox.Text.Contains("OpenAI") ||
                displayNameTextBox.Text.Contains("Custom"))
            {
                if (providerTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    var providerType = selectedItem.Tag?.ToString() ?? "";
                    var modelText = modelComboBox.Text;

                    displayNameTextBox.Text = providerType switch
                    {
                        "NvidiaBuild" => !string.IsNullOrEmpty(modelText) ? $"NVIDIA {modelText}" : "NVIDIA OCR",
                        "Gemini" => !string.IsNullOrEmpty(modelText) ? $"Gemini {modelText}" : "Gemini Vision",
                        "OpenAI" => !string.IsNullOrEmpty(modelText) ? $"OpenAI {modelText}" : "OpenAI Vision",
                        "Custom" => "Custom OCR Endpoint",
                        _ => "AI OCR Provider"
                    };
                }
            }
        }

        private void UpdateRateLimitDefault()
        {
            if (providerTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var providerType = selectedItem.Tag?.ToString() ?? "";
                var model = modelComboBox.Text;

                var defaultRpm = AIProviderDefaults.GetDefaultRequestsPerMinute(providerType, model);
                requestsPerMinuteTextBox.Text = defaultRpm.ToString();

                // Update hint text
                rateLimitHintTextBlock.Text = $"Free tier default for {(!string.IsNullOrEmpty(model) ? model : providerType)}";
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs(out var validationError))
            {
                ShowTestResult(false, validationError);
                return;
            }

            testConnectionButton.IsEnabled = false;
            testResultTextBlock.Visibility = Visibility.Collapsed;

            try
            {
                var testConfig = CreateConfigurationFromInputs();
                ShowTestResult(null, "Testing connection...");

                // Get the appropriate AI service
                var aiService = GetAIService(testConfig);
                if (aiService == null)
                {
                    ShowTestResult(false, $"Provider '{testConfig.ProviderType}' is not available or not implemented yet.");
                    return;
                }

                // Configure the service
                ConfigureAIService(aiService, testConfig);

                // Test connection
                var success = await aiService.TestConnectionAsync();

                if (success)
                {
                    ShowTestResult(true, "✓ Connection successful! Provider is configured correctly.");
                }
                else
                {
                    ShowTestResult(false, "✗ Connection failed. Check your API key and settings.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error testing AI provider connection");
                ShowTestResult(false, $"✗ Error: {ex.Message}");
            }
            finally
            {
                testConnectionButton.IsEnabled = true;
            }
        }

        private IAIProvider? GetAIService(AIProviderConfiguration config)
        {
            try
            {
                return config.ProviderType switch
                {
                    "NvidiaBuild" => ServiceLocator.GetService<INvidiaOCRService>(),
                    "Gemini" => new BlackoutScanner.Services.GeminiOCRService(),
                    // Future providers:
                    // "OpenAI" => ServiceLocator.GetService<IOpenAIService>(),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to get AI service for provider '{config.ProviderType}'");
                return null;
            }
        }

        private void ConfigureAIService(IAIProvider service, AIProviderConfiguration config)
        {
            if (service is INvidiaOCRService nvidiaService)
            {
                nvidiaService.UpdateConfiguration(config.ApiKey, config.Model);
            }
            else if (service is BlackoutScanner.Services.GeminiOCRService geminiService)
            {
                geminiService.UpdateConfiguration(config.ApiKey, config.Model);
            }
            // Future: Handle other providers
        }

        private void ShowTestResult(bool? success, string message)
        {
            testResultTextBlock.Text = message;
            testResultTextBlock.Visibility = Visibility.Visible;

            if (success.HasValue)
            {
                testResultTextBlock.Foreground = success.Value
                    ? System.Windows.Media.Brushes.Green
                    : System.Windows.Media.Brushes.Red;
            }
            else
            {
                testResultTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs(out var validationError))
            {
                MessageBox.Show(validationError, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _configuration = CreateConfigurationFromInputs();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInputs(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(displayNameTextBox.Text))
            {
                error = "Display Name is required.";
                return false;
            }

            if (providerTypeComboBox.SelectedItem == null)
            {
                error = "Provider Type must be selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(modelComboBox.Text))
            {
                error = "Model is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(apiKeyPasswordBox.Password))
            {
                error = "API Key is required.";
                return false;
            }

            if (!int.TryParse(priorityTextBox.Text, out var priority) || priority < 1)
            {
                error = "Priority must be a number greater than 0.";
                return false;
            }

            if (!int.TryParse(requestsPerMinuteTextBox.Text, out var rpm) || rpm < 1)
            {
                error = "Requests Per Minute must be a number greater than 0.";
                return false;
            }

            // Custom provider validation
            if (providerTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "Custom")
            {
                if (string.IsNullOrWhiteSpace(endpointUrlTextBox.Text))
                {
                    error = "Endpoint URL is required for custom providers.";
                    return false;
                }

                if (!Uri.TryCreate(endpointUrlTextBox.Text, UriKind.Absolute, out _))
                {
                    error = "Endpoint URL must be a valid URL.";
                    return false;
                }
            }

            return true;
        }

        private AIProviderConfiguration CreateConfigurationFromInputs()
        {
            var selectedItem = (ComboBoxItem)providerTypeComboBox.SelectedItem;
            var providerType = selectedItem.Tag?.ToString() ?? "";

            var config = new AIProviderConfiguration
            {
                Id = _configuration?.Id ?? Guid.NewGuid(),
                ProviderType = providerType,
                DisplayName = displayNameTextBox.Text.Trim(),
                Model = modelComboBox.Text.Trim(),
                ApiKey = apiKeyPasswordBox.Password,
                Priority = int.Parse(priorityTextBox.Text),
                IsEnabled = isEnabledCheckBox.IsChecked ?? true,
                RequestsPerMinute = int.Parse(requestsPerMinuteTextBox.Text),
                AdditionalSettings = new Dictionary<string, string>()
            };

            // Store custom endpoint settings
            if (providerType == "Custom" && !string.IsNullOrWhiteSpace(endpointUrlTextBox.Text))
            {
                config.AdditionalSettings["EndpointUrl"] = endpointUrlTextBox.Text.Trim();
            }

            return config;
        }
    }
}
