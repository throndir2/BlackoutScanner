using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;
using Serilog;
using System.IO;
using System.Text;

namespace BlackoutScanner
{
    public class DataManager : IDataManager
    {
        private readonly IFileSystem _fileSystem;
        private readonly string jsonFilePath;
        private readonly string invalidJsonFilePath;
        private bool hasUnsavedChanges = false;

        public Dictionary<string, DataRecord> DataRecordDictionary { get; private set; } = new Dictionary<string, DataRecord>();

        public DataManager(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            jsonFilePath = "data_records.json";
            invalidJsonFilePath = "invalid_data_records.json";
            LoadDataRecords(); // Load data upon instantiation
        }

        // Constructor for backward compatibility/testing
        public DataManager(IFileSystem fileSystem, string filePath, string invalidFilePath) : this(fileSystem)
        {
            jsonFilePath = filePath;
            invalidJsonFilePath = invalidFilePath;
            LoadDataRecords();
        }

        private void LoadDataRecords()
        {
            try
            {
                if (_fileSystem.FileExists(jsonFilePath))
                {
                    string jsonData = _fileSystem.ReadAllText(jsonFilePath);
                    var dataRecordList = JsonConvert.DeserializeObject<List<DataRecord>>(jsonData) ?? new List<DataRecord>();

                    DataRecordDictionary.Clear();

                    foreach (var record in dataRecordList)
                    {
                        // This assumes the hash can be regenerated from the loaded data.
                        // A better approach might be to store the hash with the record data.
                        // For now, we'll skip adding them to the dictionary if we can't generate a hash,
                        // as we don't have the profile context here.
                    }

                    Log.Information("Data records loaded successfully. Note: Hash regeneration requires a profile context.");
                }
                else
                {
                    Log.Information("No existing data found. Starting fresh.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load data records: {ex.Message}");
            }
        }

        public void LoadDataRecordsWithProfile(GameProfile profile)
        {
            try
            {
                string safeProfileName = GetSafeFileName(profile.ProfileName);
                string jsonFileName = $"data_records_{safeProfileName}.json";

                // Try new filename format first
                if (_fileSystem.FileExists(jsonFileName))
                {
                    string jsonData = _fileSystem.ReadAllText(jsonFileName);
                    var dataRecordList = JsonConvert.DeserializeObject<List<DataRecord>>(jsonData) ?? new List<DataRecord>();

                    DataRecordDictionary.Clear();

                    foreach (var record in dataRecordList)
                    {
                        // Now we can properly generate the hash with the profile context
                        var hash = GenerateDataHash(record, profile);
                        if (!string.IsNullOrEmpty(hash) && !hash.All(c => c == '-'))
                        {
                            DataRecordDictionary[hash] = record;
                        }
                    }

                    Log.Information($"Data records loaded successfully for profile '{profile.ProfileName}'. Loaded {DataRecordDictionary.Count} records.");
                }
                // Fall back to old filename for backward compatibility
                else if (_fileSystem.FileExists(jsonFilePath))
                {
                    string jsonData = _fileSystem.ReadAllText(jsonFilePath);
                    var dataRecordList = JsonConvert.DeserializeObject<List<DataRecord>>(jsonData) ?? new List<DataRecord>();

                    DataRecordDictionary.Clear();

                    foreach (var record in dataRecordList)
                    {
                        // Set missing properties for old data
                        if (string.IsNullOrEmpty(record.GameProfile))
                        {
                            record.GameProfile = profile.ProfileName;
                        }

                        var hash = GenerateDataHash(record, profile);
                        if (!string.IsNullOrEmpty(hash) && !hash.All(c => c == '-'))
                        {
                            DataRecordDictionary[hash] = record;
                        }
                    }

                    Log.Information($"Data records loaded from legacy file. Loaded {DataRecordDictionary.Count} records.");
                }
                else
                {
                    Log.Information("No existing data found. Starting fresh.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load data records with profile: {ex.Message}");
            }
        }

        public bool HasUnsavedChanges() => hasUnsavedChanges;

        public void MarkAsUnsaved()
        {
            hasUnsavedChanges = true;
        }

        public void SaveDataRecordsAsJson(GameProfile profile, string? exportPath = null)
        {
            try
            {
                // Generate filename with profile name
                string safeProfileName = GetSafeFileName(profile.ProfileName);

                // Use the export path if provided
                string directory = exportPath ?? Directory.GetCurrentDirectory();

                // Ensure the directory exists
                Directory.CreateDirectory(directory);

                string jsonFileName = Path.Combine(directory, $"data_records_{safeProfileName}.json");

                string jsonData = JsonConvert.SerializeObject(DataRecordDictionary.Values, Formatting.Indented);
                _fileSystem.WriteAllText(jsonFileName, jsonData);

                Log.Information($"JSON data records saved successfully for profile '{profile.ProfileName}' to {jsonFileName}.");
                hasUnsavedChanges = false; // Reset after save
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save JSON data records: {ex.Message}");
            }
        }

        public void SaveDataRecordsAsTsv(GameProfile profile, string? exportPath = null)
        {
            try
            {
                // Generate TSV files per category
                SaveTsvFilesByCategory(DataRecordDictionary.Values.ToList(), profile, exportPath);

                Log.Information($"TSV data records saved successfully for profile '{profile.ProfileName}'.");
                hasUnsavedChanges = false; // Reset after save
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save TSV data records: {ex.Message}");
            }
        }

        // Keep the original method for backward compatibility or for saving both at once (e.g., on app close)
        public void SaveDataRecords(GameProfile profile, string? exportPath = null)
        {
            SaveDataRecordsAsJson(profile, exportPath);
            SaveDataRecordsAsTsv(profile, exportPath);
        }

        private void SaveTsvFilesByCategory(List<DataRecord> dataList, GameProfile profile, string? exportPath = null)
        {
            // Use the export path if provided
            string directory = exportPath ?? Directory.GetCurrentDirectory();

            // Ensure the directory exists
            Directory.CreateDirectory(directory);

            // Group records by category
            var recordsByCategory = dataList.GroupBy(r => r.Category);

            foreach (var categoryGroup in recordsByCategory)
            {
                var categoryName = categoryGroup.Key;
                var categoryRecords = categoryGroup.ToList();

                if (string.IsNullOrEmpty(categoryName) || categoryRecords.Count == 0)
                    continue;

                // Generate filename with profile and category name
                string safeProfileName = GetSafeFileName(profile.ProfileName);
                string safeCategoryName = GetSafeFileName(categoryName);
                string tsvFileName = Path.Combine(directory, $"data_records_{safeProfileName}_{safeCategoryName}.tsv");

                // Find the category definition to get field order
                var category = profile.Categories.FirstOrDefault(c => c.Name == categoryName);
                if (category != null)
                {
                    string tsvContent = ConvertCategoryRecordsToTsv(categoryRecords, category);
                    _fileSystem.WriteAllText(tsvFileName, tsvContent);
                    Log.Information($"Saved TSV for category '{categoryName}' in profile '{profile.ProfileName}' to {tsvFileName}");
                }
            }
        }

        private string ConvertCategoryRecordsToTsv(List<DataRecord> records, CaptureCategory category)
        {
            var sb = new StringBuilder();
            var headers = new List<string>();

            // Add headers from the category fields
            foreach (var field in category.Fields)
            {
                headers.Add(field.Name);
            }
            headers.Add("ScanDate");

            sb.AppendLine(string.Join("\t", headers));

            foreach (var record in records)
            {
                var values = new List<string>();
                foreach (var header in headers)
                {
                    if (header == "ScanDate")
                    {
                        values.Add(record.ScanDate.ToString("g"));
                    }
                    else if (record.Fields.TryGetValue(header, out var value))
                    {
                        values.Add(value?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        values.Add(string.Empty);
                    }
                }
                sb.AppendLine(string.Join("\t", values));
            }

            return sb.ToString();
        }

        private string GetSafeFileName(string fileName)
        {
            // Remove invalid characters from filename
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeFileName = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return safeFileName.Replace(" ", "_");
        }

        public string GenerateDataHash(DataRecord record, GameProfile profile)
        {
            var keyFields = new List<string>();
            foreach (var category in profile.Categories)
            {
                foreach (var field in category.Fields)
                {
                    if (field.IsKeyField && record.Fields.TryGetValue(field.Name, out var value))
                    {
                        keyFields.Add(value?.ToString() ?? string.Empty);
                    }
                }
            }
            return string.Join("-", keyFields);
        }

        public void AddOrUpdateRecord(DataRecord newRecord, GameProfile profile)
        {
            var hash = GenerateDataHash(newRecord, profile);
            if (string.IsNullOrEmpty(hash) || hash.All(c => c == '-'))
            {
                Log.Warning("Could not generate a valid hash for the data record. Please ensure at least one field is marked as a 'Key Field' in the profile configuration.");
                return;
            }

            if (DataRecordDictionary.ContainsKey(hash))
            {
                // Update existing record data
                var existingRecord = DataRecordDictionary[hash];
                foreach (var field in newRecord.Fields)
                {
                    existingRecord.Fields[field.Key] = field.Value;
                }
                existingRecord.ScanDate = newRecord.ScanDate;
            }
            else
            {
                // Add new record data
                DataRecordDictionary.Add(hash, newRecord);
            }

            hasUnsavedChanges = true; // Mark as having changes
        }

        // Add this method to handle record removal
        public void RemoveRecord(string hash)
        {
            if (DataRecordDictionary.ContainsKey(hash))
            {
                DataRecordDictionary.Remove(hash);
                hasUnsavedChanges = true;
            }
        }

        // Implement the interface method to handle record key changes
        public void UpdateRecordKey(string oldHash, DataRecord updatedRecord, GameProfile profile)
        {
            if (DataRecordDictionary.ContainsKey(oldHash))
            {
                string newHash = GenerateDataHash(updatedRecord, profile);
                if (!string.IsNullOrEmpty(newHash) && !newHash.All(c => c == '-'))
                {
                    DataRecordDictionary.Remove(oldHash);
                    DataRecordDictionary[newHash] = updatedRecord;
                    hasUnsavedChanges = true;
                }
                else
                {
                    Log.Warning("Could not generate a valid new hash for the updated record. Record not updated.");
                }
            }
        }

        public void SaveInvalidDataRecords(Dictionary<string, DataRecord> invalidDataRecords)
        {
            try
            {
                if (invalidDataRecords != null && invalidDataRecords.Count > 0)
                {
                    string jsonData = JsonConvert.SerializeObject(invalidDataRecords.Values, Formatting.Indented);
                    _fileSystem.WriteAllText(invalidJsonFilePath, jsonData);
                    Log.Information("Invalid data records saved successfully.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save invalid data records: {ex.Message}");
            }
        }

        private string ConvertListToTsv(List<DataRecord> dataList, GameProfile profile)
        {
            var sb = new StringBuilder();
            var headers = new List<string>();

            // Dynamically get headers from the profile
            foreach (var category in profile.Categories)
            {
                foreach (var field in category.Fields)
                {
                    headers.Add(field.Name);
                }
            }
            headers.Add("ScanDate");

            sb.AppendLine(string.Join("\t", headers));

            foreach (var record in dataList)
            {
                var values = new List<string>();
                foreach (var header in headers)
                {
                    if (header == "ScanDate")
                    {
                        values.Add(record.ScanDate.ToString("g"));
                    }
                    else if (record.Fields.TryGetValue(header, out var value))
                    {
                        values.Add(value?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        values.Add(string.Empty);
                    }
                }
                sb.AppendLine(string.Join("\t", values));
            }

            return sb.ToString();
        }
    }
}
