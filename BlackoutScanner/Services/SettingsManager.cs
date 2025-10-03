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
    }
}
