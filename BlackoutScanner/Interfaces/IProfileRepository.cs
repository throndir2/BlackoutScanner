using System.Collections.Generic;
using System.Threading.Tasks;
using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface IProfileRepository
    {
        Task<IEnumerable<GameProfile>> GetAllProfilesAsync();
        Task<GameProfile?> GetProfileByNameAsync(string profileName);
        Task SaveProfileAsync(GameProfile profile);
        Task DeleteProfileAsync(string profileName);
        Task<string?> GetActiveProfileNameAsync();
        Task SetActiveProfileNameAsync(string profileName);
    }
}
