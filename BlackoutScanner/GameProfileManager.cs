using BlackoutScanner.Models;
using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace BlackoutScanner
{
    public class GameProfileManager
    {
        private readonly string profilesDirectory;
        private readonly string activeProfileFilePath;
        public List<GameProfile> Profiles { get; private set; } = new List<GameProfile>();
        public GameProfile? ActiveProfile { get; set; }

        public GameProfileManager(string directory = "profiles")
        {
            profilesDirectory = directory;
            activeProfileFilePath = Path.Combine(profilesDirectory, "_active_profile.txt");
            Log.Information($"GameProfileManager: Looking for profiles in directory: {Path.GetFullPath(profilesDirectory)}");

            if (!Directory.Exists(profilesDirectory))
            {
                Log.Information($"GameProfileManager: Creating profiles directory: {profilesDirectory}");
                Directory.CreateDirectory(profilesDirectory);
            }
            LoadProfiles();
            LoadActiveProfile();
        }

        public void LoadProfiles()
        {
            Profiles.Clear();
            var profileFiles = Directory.GetFiles(profilesDirectory, "*.json");
            Log.Information($"GameProfileManager: Found {profileFiles.Length} profile files in {profilesDirectory}");

            foreach (var file in profileFiles)
            {
                try
                {
                    Log.Information($"GameProfileManager: Loading profile from file: {file}");
                    var json = File.ReadAllText(file);
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
                if (File.Exists(activeProfileFilePath))
                {
                    var activeProfileName = File.ReadAllText(activeProfileFilePath).Trim();
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
                    File.WriteAllText(activeProfileFilePath, ActiveProfile.ProfileName);
                    Log.Information($"GameProfileManager: Saved active profile '{ActiveProfile.ProfileName}'");
                }
                else if (File.Exists(activeProfileFilePath))
                {
                    File.Delete(activeProfileFilePath);
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
            var filePath = Path.Combine(profilesDirectory, $"{profile.ProfileName}.json");
            var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
            File.WriteAllText(filePath, json);

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
                var filePath = Path.Combine(profilesDirectory, $"{profile.ProfileName}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
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
