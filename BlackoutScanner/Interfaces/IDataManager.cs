using System.Collections.Generic;
using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface IDataManager
    {
        Dictionary<string, DataRecord> DataRecordDictionary { get; }

        void LoadDataRecordsWithProfile(GameProfile profile);
        bool HasUnsavedChanges();
        void MarkAsUnsaved();
        void SaveDataRecordsAsJson(GameProfile profile);
        void SaveDataRecordsAsTsv(GameProfile profile);
        void SaveDataRecords(GameProfile profile);
        string GenerateDataHash(DataRecord record, GameProfile profile);
        void AddOrUpdateRecord(DataRecord newRecord, GameProfile profile);
        void RemoveRecord(string hash);
        void UpdateRecordKey(string oldHash, DataRecord updatedRecord, GameProfile profile);
    }
}
