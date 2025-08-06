using BlackoutScanner.Models;
using BlackoutScanner.Interfaces;
using Newtonsoft.Json;
using Serilog;
using System;
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
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loadedSettings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                        Log.Information($"Settings loaded from {_settingsFilePath}");
                    }
                }
                else
                {
                    // First run - save default settings
                    SaveSettings();
                    Log.Information("Default settings created");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load settings, using defaults");
                _settings = new AppSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                Log.Information("Settings saved");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
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
