using System.Collections.Generic;
using System.Threading.Tasks;
using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface IDataRecordRepository
    {
        Task<Dictionary<string, DataRecord>> LoadRecordsAsync(string fileName);
        Task SaveRecordsAsJsonAsync(Dictionary<string, DataRecord> records, string fileName);
        Task SaveRecordsAsTsvAsync(IEnumerable<DataRecord> records, GameProfile profile, string fileName);
    }
}
