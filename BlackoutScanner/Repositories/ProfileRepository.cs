using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;

namespace BlackoutScanner.Repositories
{
    public class ProfileRepository : IProfileRepository
    {
        private readonly IFileSystem _fileSystem;
        private readonly string _profilesDirectory;
        private readonly string _activeProfilePath;

        public ProfileRepository(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string basePath = Path.Combine(appDataPath, "BlackoutScanner");

            _profilesDirectory = Path.Combine(basePath, "profiles");
            _activeProfilePath = Path.Combine(basePath, "active_profile.txt");

            if (!_fileSystem.DirectoryExists(_profilesDirectory))
            {
                _fileSystem.CreateDirectory(_profilesDirectory);
            }
        }

        public async Task<IEnumerable<GameProfile>> GetAllProfilesAsync()
        {
            List<GameProfile> profiles = new List<GameProfile>();

            var files = _fileSystem.GetFiles(_profilesDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = _fileSystem.ReadAllText(file);
                    var profile = JsonConvert.DeserializeObject<GameProfile>(json);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception)
                {
                    // Log error or handle invalid profiles
                }
            }

            return profiles;
        }

        public async Task<GameProfile?> GetProfileByNameAsync(string profileName)
        {
            string profilePath = Path.Combine(_profilesDirectory, $"{profileName}.json");

            if (!_fileSystem.FileExists(profilePath))
            {
                return null;
            }

            try
            {
                string json = _fileSystem.ReadAllText(profilePath);
                return JsonConvert.DeserializeObject<GameProfile>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task SaveProfileAsync(GameProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileName))
            {
                throw new ArgumentException("Profile name cannot be empty");
            }

            string profilePath = Path.Combine(_profilesDirectory, $"{profile.ProfileName}.json");
            string json = JsonConvert.SerializeObject(profile, Formatting.Indented);

            _fileSystem.WriteAllText(profilePath, json);
        }

        public async Task DeleteProfileAsync(string profileName)
        {
            string profilePath = Path.Combine(_profilesDirectory, $"{profileName}.json");

            if (_fileSystem.FileExists(profilePath))
            {
                _fileSystem.DeleteFile(profilePath);
            }

            // If the deleted profile was active, clear the active profile
            string? activeProfile = await GetActiveProfileNameAsync();
            if (activeProfile == profileName)
            {
                await SetActiveProfileNameAsync(string.Empty);
            }
        }

        public async Task<string?> GetActiveProfileNameAsync()
        {
            if (!_fileSystem.FileExists(_activeProfilePath))
            {
                return null;
            }

            try
            {
                string activeProfile = _fileSystem.ReadAllText(_activeProfilePath);
                return string.IsNullOrWhiteSpace(activeProfile) ? null : activeProfile;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task SetActiveProfileNameAsync(string profileName)
        {
            _fileSystem.WriteAllText(_activeProfilePath, profileName);
        }
    }
}
