using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using BlackoutScanner.Utilities;
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
                // Get the UseLocalTimeInExports setting
                bool useLocalTime = false;
                if (ServiceLocator.IsInitialized)
                {
                    var settingsManager = ServiceLocator.GetService<ISettingsManager>();
                    useLocalTime = settingsManager?.Settings?.UseLocalTimeInExports ?? false;
                }

                // Generate filename with profile name
                string safeProfileName = GetSafeFileName(profile.ProfileName);

                // Use the export path if provided
                string directory = exportPath ?? Directory.GetCurrentDirectory();

                // Ensure the directory exists
                Directory.CreateDirectory(directory);

                string jsonFileName = Path.Combine(directory, $"data_records_{safeProfileName}.json");

                // Create a copy of the records with adjusted timestamps if needed
                var recordsToExport = DataRecordDictionary.Values.Select(r =>
                {
                    if (!useLocalTime)
                        return r; // Return original if using UTC

                    // Create a modified copy with local time
                    var copy = new DataRecord
                    {
                        Category = r.Category,
                        GameProfile = r.GameProfile,
                        Fields = new Dictionary<string, object>(r.Fields),
                        ScanDate = r.ScanDate.ToLocalTime() // Convert to local time
                    };
                    return copy;
                }).ToList();

                // Add export metadata
                var exportData = new
                {
                    Profile = profile.ProfileName,
                    ExportDate = useLocalTime ? DateTime.Now : DateTime.UtcNow,
                    TimeFormat = useLocalTime ? "Local" : "UTC",
                    Records = recordsToExport
                };

                string jsonData = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                _fileSystem.WriteAllText(jsonFileName, jsonData);

                string timeFormat = useLocalTime ? "Local time" : "UTC";
                Log.Information($"JSON data records saved successfully for profile '{profile.ProfileName}' to {jsonFileName} ({timeFormat}).");
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
                // Get the UseLocalTimeInExports setting
                bool useLocalTime = false;
                if (ServiceLocator.IsInitialized)
                {
                    var settingsManager = ServiceLocator.GetService<ISettingsManager>();
                    useLocalTime = settingsManager?.Settings?.UseLocalTimeInExports ?? false;
                }

                // Generate TSV files per category
                SaveTsvFilesByCategory(DataRecordDictionary.Values.ToList(), profile, exportPath, useLocalTime);

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

        private void SaveTsvFilesByCategory(List<DataRecord> dataList, GameProfile profile, string? exportPath = null, bool useLocalTime = false)
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
                    string tsvContent = ConvertCategoryRecordsToTsv(categoryRecords, category, useLocalTime);
                    _fileSystem.WriteAllText(tsvFileName, tsvContent);
                    string timeFormat = useLocalTime ? "Local time" : "UTC";
                    Log.Information($"Saved TSV for category '{categoryName}' in profile '{profile.ProfileName}' to {tsvFileName} ({timeFormat})");
                }
            }
        }

        private string ConvertCategoryRecordsToTsv(List<DataRecord> records, CaptureCategory category, bool useLocalTime = false)
        {
            var sb = new StringBuilder();
            var headers = new List<string>();

            // Add headers from the category fields
            foreach (var field in category.Fields)
            {
                headers.Add(field.Name);
            }

            // Add row index if this is a multi-entity category
            if (category.IsMultiEntity)
            {
                headers.Add("RowIndex");
                headers.Add("GroupId");
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
                        // Convert to local time if specified
                        var scanDate = useLocalTime ? record.ScanDate.ToLocalTime() : record.ScanDate;
                        values.Add(scanDate.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                    else if (header == "RowIndex" && record.EntityIndex.HasValue)
                    {
                        values.Add((record.EntityIndex.Value + 1).ToString()); // 1-based for user display
                    }
                    else if (header == "GroupId" && record.GroupId.HasValue)
                    {
                        values.Add(record.GroupId.Value.ToString());
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
            try
            {
                // Find the category for this record
                var category = profile.Categories.FirstOrDefault(c => c.Name == record.Category);
                if (category == null)
                {
                    Log.Warning($"Category '{record.Category}' not found in profile '{profile.ProfileName}'");
                    return string.Empty;
                }

                // Get all key fields for this category
                var categoryKeyFields = category.Fields.Where(f => f.IsKeyField).Select(f => f.Name).ToList();

                if (!categoryKeyFields.Any())
                {
                    Log.Warning($"No key fields defined for category '{category.Name}' in profile '{profile.ProfileName}'");
                    return string.Empty;
                }

                // Build the hash from key field values
                var hashParts = new List<string>();

                foreach (var keyFieldName in categoryKeyFields)
                {
                    if (record.Fields.TryGetValue(keyFieldName, out var value))
                    {
                        // Convert the value to string for hashing
                        var stringValue = value?.ToString() ?? "";

                        // Only add non-empty values to the hash
                        if (!string.IsNullOrWhiteSpace(stringValue))
                        {
                            hashParts.Add($"{keyFieldName}:{stringValue}");
                            Log.Debug($"Adding key field to hash: {keyFieldName} = {stringValue}");
                        }
                        else
                        {
                            Log.Debug($"Key field '{keyFieldName}' has empty value, skipping from hash");
                        }
                    }
                    else
                    {
                        Log.Debug($"Key field '{keyFieldName}' not found in record fields");
                    }
                }

                // If we have no hash parts, we can't generate a valid hash
                if (!hashParts.Any())
                {
                    Log.Warning($"No valid key field values found for generating hash. Category: {category.Name}, Available fields: {string.Join(", ", record.Fields.Keys)}");
                    return string.Empty;
                }

                // Create a deterministic hash
                var combinedString = string.Join("|", hashParts.OrderBy(x => x)); // Sort for consistency
                combinedString = $"{profile.ProfileName}|{record.Category}|{combinedString}";

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedString));
                    var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    Log.Debug($"Generated hash: {hash} from: {combinedString}");
                    return hash;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error generating hash for data record");
                return string.Empty;
            }
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
