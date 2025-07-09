using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace BlackoutScanner
{
    public class GameProfileManager : IGameProfileManager
    {
        private readonly IFileSystem _fileSystem;
        private readonly string profilesDirectory;
        private readonly string activeProfileFilePath;
        public List<GameProfile> Profiles { get; private set; } = new List<GameProfile>();
        public GameProfile? ActiveProfile { get; set; }

        public GameProfileManager(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            profilesDirectory = "profiles";
            activeProfileFilePath = _fileSystem.Combine(profilesDirectory, "_active_profile.txt");
            Log.Information($"GameProfileManager: Looking for profiles in directory: {Path.GetFullPath(profilesDirectory)}");

            if (!_fileSystem.DirectoryExists(profilesDirectory))
            {
                Log.Information($"GameProfileManager: Creating profiles directory: {profilesDirectory}");
                _fileSystem.CreateDirectory(profilesDirectory);
            }
            LoadProfiles();
            LoadActiveProfile();
        }

        // Constructor for backward compatibility/testing
        public GameProfileManager(IFileSystem fileSystem, string directory) : this(fileSystem)
        {
            profilesDirectory = directory;
            activeProfileFilePath = _fileSystem.Combine(profilesDirectory, "_active_profile.txt");
            LoadProfiles();
            LoadActiveProfile();
        }

        public void LoadProfiles()
        {
            Profiles.Clear();
            var profileFiles = _fileSystem.GetFiles(profilesDirectory, "*.json");
            Log.Information($"GameProfileManager: Found {profileFiles.Length} profile files in {profilesDirectory}");

            foreach (var file in profileFiles)
            {
                try
                {
                    Log.Information($"GameProfileManager: Loading profile from file: {file}");
                    var json = _fileSystem.ReadAllText(file);
                    var profile = JsonConvert.DeserializeObject<GameProfile>(json);
                    if (profile != null)
                    {
                        Profiles.Add(profile);
                        Log.Information($"GameProfileManager: Successfully loaded profile '{profile.ProfileName}' with window title '{profile.GameWindowTitle}'");
                    }
                    else
                    {
                        Log.Warning($"GameProfileManager: Failed to deserialize profile from file: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to load profile '{file}': {ex.Message}");
                }
            }

            Log.Information($"GameProfileManager: Total profiles loaded: {Profiles.Count}");
        }

        private void LoadActiveProfile()
        {
            try
            {
                if (_fileSystem.FileExists(activeProfileFilePath))
                {
                    var activeProfileName = _fileSystem.ReadAllText(activeProfileFilePath).Trim();
                    var profile = Profiles.FirstOrDefault(p => p.ProfileName == activeProfileName);
                    if (profile != null)
                    {
                        ActiveProfile = profile;
                        Log.Information($"GameProfileManager: Restored active profile '{activeProfileName}'");
                    }
                    else
                    {
                        Log.Warning($"GameProfileManager: Could not find previously active profile '{activeProfileName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GameProfileManager: Failed to load active profile: {ex.Message}");
            }
        }

        private void SaveActiveProfile()
        {
            try
            {
                if (ActiveProfile != null)
                {
                    _fileSystem.WriteAllText(activeProfileFilePath, ActiveProfile.ProfileName);
                    Log.Information($"GameProfileManager: Saved active profile '{ActiveProfile.ProfileName}'");
                }
                else if (_fileSystem.FileExists(activeProfileFilePath))
                {
                    _fileSystem.DeleteFile(activeProfileFilePath);
                    Log.Information("GameProfileManager: Cleared active profile");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GameProfileManager: Failed to save active profile: {ex.Message}");
            }
        }

        public void SaveProfile(GameProfile profile)
        {
            var filePath = _fileSystem.Combine(profilesDirectory, $"{profile.ProfileName}.json");
            var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
            _fileSystem.WriteAllText(filePath, json);

            // Update existing profile or add new one
            var existingProfile = Profiles.FirstOrDefault(p => p.ProfileName == profile.ProfileName);
            if (existingProfile != null)
            {
                Profiles[Profiles.IndexOf(existingProfile)] = profile;
            }
            else
            {
                Profiles.Add(profile);
            }

            // If this was the active profile, update the reference
            if (ActiveProfile != null && ActiveProfile.ProfileName == profile.ProfileName)
            {
                ActiveProfile = profile;
            }

            Log.Information($"GameProfileManager: Saved profile '{profile.ProfileName}' to {filePath}");
        }

        public void DeleteProfile(GameProfile profile)
        {
            try
            {
                var filePath = _fileSystem.Combine(profilesDirectory, $"{profile.ProfileName}.json");
                if (_fileSystem.FileExists(filePath))
                {
                    _fileSystem.DeleteFile(filePath);
                    Profiles.Remove(profile);

                    // If we deleted the active profile, clear it
                    if (ActiveProfile == profile)
                    {
                        ActiveProfile = null;
                        SaveActiveProfile();
                    }

                    Log.Information($"Deleted profile: {profile.ProfileName}");
                }
                else
                {
                    Log.Warning($"Attempted to delete a profile file that does not exist: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to delete profile: {profile.ProfileName}");
            }
        }

        public void SetActiveProfile(GameProfile profile)
        {
            ActiveProfile = profile;
            SaveActiveProfile();
            Log.Information($"GameProfileManager: Set active profile to '{profile?.ProfileName ?? "null"}'");
        }
    }
}
