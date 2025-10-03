using BlackoutScanner.Models;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Infrastructure;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace BlackoutScanner.Services
{
    public class SettingsManager : ISettingsManager
    {
        private readonly string _settingsFilePath;
        private AppSettings _settings;

        public AppSettings Settings => _settings;

        public SettingsManager()
        {
            // Store settings in AppData/Local/BlackoutScanner
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlackoutScanner"
            );

            // Ensure the directory exists
            Directory.CreateDirectory(appDataPath);

            _settingsFilePath = Path.Combine(appDataPath, "appsettings.json");
            _settings = new AppSettings();

            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                Log.Information("[SettingsManager.LoadSettings] Starting to load settings...");

                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    Log.Debug($"[SettingsManager.LoadSettings] Raw JSON: {json}");

                    var loadedSettings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (loadedSettings != null)
                    {
                        Log.Information($"[SettingsManager.LoadSettings] Deserialized settings: SaveDebugImages={loadedSettings.SaveDebugImages}, LogLevel={loadedSettings.LogLevel}, SelectedLanguages count={loadedSettings.SelectedLanguages?.Count ?? 0}");
                        Log.Information($"[SettingsManager.LoadSettings] Deserialized SelectedLanguages: [{string.Join(", ", loadedSettings.SelectedLanguages ?? new List<string>())}]");

                        // Deduplicate languages in case of corrupted config file
                        if (loadedSettings.SelectedLanguages != null)
                        {
                            var originalCount = loadedSettings.SelectedLanguages.Count;
                            loadedSettings.SelectedLanguages = loadedSettings.SelectedLanguages.Distinct().ToList();
                            if (originalCount != loadedSettings.SelectedLanguages.Count)
                            {
                                Log.Warning($"[SettingsManager.LoadSettings] Deduplicated SelectedLanguages from {originalCount} to {loadedSettings.SelectedLanguages.Count} items");
                            }
                        }

                        _settings = loadedSettings;
                        Log.Information($"[SettingsManager.LoadSettings] Settings loaded from {_settingsFilePath}");
                        Log.Information($"[SettingsManager.LoadSettings] AFTER assignment - _settings.SelectedLanguages count={_settings.SelectedLanguages?.Count ?? 0}: [{string.Join(", ", _settings.SelectedLanguages ?? new List<string>())}]");

                        // Migrate old single-provider settings to new multi-provider format
                        MigrateAIProviderSettings();
                    }
                    else
                    {
                        Log.Warning("[SettingsManager.LoadSettings] Loaded settings was null");
                    }
                }
                else
                {
                    // First run - save default settings
                    Log.Information($"[SettingsManager.LoadSettings] Settings file not found at {_settingsFilePath}, creating default");
                    SaveSettings();
                    Log.Information("[SettingsManager.LoadSettings] Default settings created");
                }

                // Validate AI provider settings
                ValidateAIProviderSettings();

                // Apply log level to UI sink
                if (Enum.TryParse<LogEventLevel>(_settings.LogLevel, out var logLevel))
                {
                    UISink.MinimumLevel = logLevel;
                    Log.Information($"[SettingsManager.LoadSettings] UI log level set to: {logLevel}");
                }

                // Log the state of settings after loading
                Log.Information($"[SettingsManager.LoadSettings] Current settings state: SaveDebugImages={_settings.SaveDebugImages}, LogLevel={_settings.LogLevel}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SettingsManager.LoadSettings] Failed to load settings, using defaults");
                _settings = new AppSettings();

                // Apply default log level
                if (Enum.TryParse<LogEventLevel>(_settings.LogLevel, out var logLevel))
                {
                    UISink.MinimumLevel = logLevel;
                }

                Log.Information($"[SettingsManager.LoadSettings] Created new default settings: SaveDebugImages={_settings.SaveDebugImages}, LogLevel={_settings.LogLevel}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                // Apply log level to UI sink immediately when saving
                if (Enum.TryParse<LogEventLevel>(_settings.LogLevel, out var logLevel))
                {
                    UISink.MinimumLevel = logLevel;
                    Log.Information($"[SettingsManager.SaveSettings] UI log level set to: {logLevel}");
                }

                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);

                // Before saving, check if the file already exists and what it contains
                if (File.Exists(_settingsFilePath))
                {
                    var existingJson = File.ReadAllText(_settingsFilePath);
                    try
                    {
                        var existingSettings = JsonConvert.DeserializeObject<AppSettings>(existingJson);
                        if (existingSettings != null)
                        {
                            Log.Debug($"[SettingsManager.SaveSettings] Existing SaveDebugImages={existingSettings.SaveDebugImages}");
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Log.Warning(parseEx, "[SettingsManager.SaveSettings] Could not parse existing settings file");
                    }
                }

                File.WriteAllText(_settingsFilePath, json);
                Log.Information($"[SettingsManager.SaveSettings] Settings saved to {_settingsFilePath}");

                // Verify saved file
                if (File.Exists(_settingsFilePath))
                {
                    var savedJson = File.ReadAllText(_settingsFilePath);
                    // Parse the saved JSON to verify the settings were properly saved
                    try
                    {
                        var verifiedSettings = JsonConvert.DeserializeObject<AppSettings>(savedJson);
                        if (verifiedSettings != null)
                        {
                            Log.Information($"[SettingsManager.SaveSettings] Verified SaveDebugImages={verifiedSettings.SaveDebugImages}, matches in-memory value: {verifiedSettings.SaveDebugImages == _settings.SaveDebugImages}");
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Log.Error(parseEx, "[SettingsManager.SaveSettings] Error parsing saved JSON");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SettingsManager.SaveSettings] Failed to save settings");
            }
        }

        public string GetFullExportPath()
        {
            // If the path is relative, make it relative to the application directory
            if (!Path.IsPathRooted(_settings.ExportFolder))
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.ExportFolder);
            }
            return _settings.ExportFolder;
        }

        /// <summary>
        /// Migrates old single-provider settings to new multi-provider format.
        /// Checks for obsolete AIProvider, NvidiaApiKey, etc. and converts them to AIProviders collection.
        /// </summary>
        private void MigrateAIProviderSettings()
        {
            try
            {
                // Check if migration is needed (old settings exist and new settings don't)
#pragma warning disable CS0618 // Type or member is obsolete
                bool hasOldSettings = !string.IsNullOrEmpty(_settings.AIProvider) ||
                                     !string.IsNullOrEmpty(_settings.NvidiaApiKey);
#pragma warning restore CS0618

                bool hasNewSettings = _settings.AIProviders != null && _settings.AIProviders.Any();

                if (hasOldSettings && !hasNewSettings)
                {
                    Log.Information("[SettingsManager] Migrating old single-provider AI settings to multi-provider format");

                    // Initialize the AIProviders collection if null
                    if (_settings.AIProviders == null)
                    {
                        _settings.AIProviders = new System.Collections.ObjectModel.ObservableCollection<AIProviderConfiguration>();
                    }

#pragma warning disable CS0618 // Type or member is obsolete
                    // Migrate NVIDIA settings if they exist
                    if (!string.IsNullOrEmpty(_settings.NvidiaApiKey))
                    {
                        var model = !string.IsNullOrEmpty(_settings.AIProvider) ? _settings.AIProvider : "baidu/paddleocr";
                        var nvidiaProvider = new AIProviderConfiguration
                        {
                            Id = Guid.NewGuid(),
                            ProviderType = "NvidiaBuild",
                            DisplayName = "NVIDIA Build API (Migrated)",
                            Model = model,
                            ApiKey = _settings.NvidiaApiKey,
                            Priority = 1,
                            IsEnabled = true,
                            RequestsPerMinute = BlackoutScanner.Utilities.AIProviderDefaults.GetDefaultRequestsPerMinute("NvidiaBuild", model)
                        };
                        _settings.AIProviders.Add(nvidiaProvider);
                        Log.Information($"[SettingsManager] Migrated NVIDIA provider: Model={nvidiaProvider.Model}, RPM={nvidiaProvider.RequestsPerMinute}");
                    }

                    // Migrate OpenAI settings if they exist
                    if (!string.IsNullOrEmpty(_settings.OpenAIApiKey))
                    {
                        var openAIProvider = new AIProviderConfiguration
                        {
                            Id = Guid.NewGuid(),
                            ProviderType = "OpenAI",
                            DisplayName = "OpenAI (Migrated)",
                            Model = "gpt-4o",
                            ApiKey = _settings.OpenAIApiKey,
                            Priority = 2,
                            IsEnabled = false, // Disable by default since OpenAI isn't free
                            RequestsPerMinute = BlackoutScanner.Utilities.AIProviderDefaults.GetDefaultRequestsPerMinute("OpenAI", "gpt-4o")
                        };
                        _settings.AIProviders.Add(openAIProvider);
                        Log.Information($"[SettingsManager] Migrated OpenAI provider (disabled by default), RPM={openAIProvider.RequestsPerMinute}");
                    }

                    // Migrate Gemini settings if they exist
                    if (!string.IsNullOrEmpty(_settings.GeminiApiKey))
                    {
                        var geminiProvider = new AIProviderConfiguration
                        {
                            Id = Guid.NewGuid(),
                            ProviderType = "Gemini",
                            DisplayName = "Gemini Flash (Migrated)",
                            Model = "gemini-2.5-flash",
                            ApiKey = _settings.GeminiApiKey,
                            Priority = 3,
                            IsEnabled = true,
                            RequestsPerMinute = BlackoutScanner.Utilities.AIProviderDefaults.GetDefaultRequestsPerMinute("Gemini", "gemini-2.5-flash")
                        };
                        _settings.AIProviders.Add(geminiProvider);
                        Log.Information($"[SettingsManager] Migrated Gemini provider: Model={geminiProvider.Model}, RPM={geminiProvider.RequestsPerMinute}");
                    }
#pragma warning restore CS0618

                    if (_settings.AIProviders.Any())
                    {
                        // Save the migrated settings
                        SaveSettings();
                        Log.Information($"[SettingsManager] Migration complete: {_settings.AIProviders.Count} provider(s) migrated");
                    }
                }
                else if (hasNewSettings)
                {
                    Log.Debug("[SettingsManager] New multi-provider settings already configured, skipping migration");
                }
                else
                {
                    Log.Debug("[SettingsManager] No AI provider settings to migrate");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SettingsManager] Error during AI provider settings migration");
            }
        }

        /// <summary>
        /// Validates AI provider settings for consistency and logs warnings.
        /// </summary>
        private void ValidateAIProviderSettings()
        {
            try
            {
                if (_settings.AIProviders == null || !_settings.AIProviders.Any())
                {
                    if (_settings.UseAIEnhancedOCR)
                    {
                        Log.Warning("[SettingsManager] AI Enhancement is enabled but no providers are configured");
                    }
                    return;
                }

                // Check for duplicate priorities
                var duplicatePriorities = _settings.AIProviders
                    .GroupBy(p => p.Priority)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicatePriorities.Any())
                {
                    Log.Warning($"[SettingsManager] Duplicate provider priorities found: {string.Join(", ", duplicatePriorities)}. Providers will be processed in the order they appear.");
                }

                // Check for enabled providers with missing API keys
                var providersWithoutKeys = _settings.AIProviders
                    .Where(p => p.IsEnabled && string.IsNullOrWhiteSpace(p.ApiKey))
                    .ToList();

                if (providersWithoutKeys.Any())
                {
                    Log.Warning($"[SettingsManager] {providersWithoutKeys.Count} enabled provider(s) have missing API keys: {string.Join(", ", providersWithoutKeys.Select(p => p.DisplayName))}");
                }

                // Check if UseAIEnhancedOCR is enabled but no providers are enabled
                if (_settings.UseAIEnhancedOCR)
                {
                    var enabledProviders = _settings.AIProviders.Where(p => p.IsEnabled).ToList();
                    if (!enabledProviders.Any())
                    {
                        Log.Warning("[SettingsManager] AI Enhancement is enabled but no providers are enabled");
                    }
                    else
                    {
                        Log.Information($"[SettingsManager] AI Enhancement enabled with {enabledProviders.Count} active provider(s): {string.Join(", ", enabledProviders.Select(p => $"{p.DisplayName} (Priority {p.Priority})"))}");
                    }
                }

                // Log provider configuration summary
                var sortedProviders = _settings.AIProviders.OrderBy(p => p.Priority).ToList();
                Log.Information("[SettingsManager] AI Provider Configuration Summary:");
                foreach (var provider in sortedProviders)
                {
                    var status = provider.IsEnabled ? "Enabled" : "Disabled";
                    var keyStatus = !string.IsNullOrWhiteSpace(provider.ApiKey) ? "API key set" : "No API key";
                    Log.Information($"  - Priority {provider.Priority}: {provider.DisplayName} ({provider.ProviderType}/{provider.Model}) - {status}, {keyStatus}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SettingsManager] Error during AI provider settings validation");
            }
        }
    }
}
