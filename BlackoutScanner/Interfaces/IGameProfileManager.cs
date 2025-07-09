using System.Collections.Generic;
using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface IGameProfileManager
    {
        List<GameProfile> Profiles { get; }
        GameProfile? ActiveProfile { get; set; }

        void LoadProfiles();
        void SaveProfile(GameProfile profile);
        void DeleteProfile(GameProfile profile);
        void SetActiveProfile(GameProfile profile);
    }
}
