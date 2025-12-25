using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;
using Serilog;

namespace BlackoutScanner.Repositories
{
    public class DataRecordRepository : IDataRecordRepository
    {
        private readonly IFileSystem _fileSystem;

        public DataRecordRepository(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<Dictionary<string, DataRecord>> LoadRecordsAsync(string fileName)
        {
            try
            {
                if (!_fileSystem.FileExists(fileName))
                {
                    return new Dictionary<string, DataRecord>();
                }

                string jsonContent = _fileSystem.ReadAllText(fileName);
                var records = JsonConvert.DeserializeObject<Dictionary<string, DataRecord>>(jsonContent);
                return records ?? new Dictionary<string, DataRecord>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading data records from {FileName}", fileName);
                return new Dictionary<string, DataRecord>();
            }
        }

        public async Task SaveRecordsAsJsonAsync(Dictionary<string, DataRecord> records, string fileName)
        {
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
                {
                    _fileSystem.CreateDirectory(directory);
                }

                string jsonContent = JsonConvert.SerializeObject(records, Formatting.Indented);
                _fileSystem.WriteAllText(fileName, jsonContent);
                Log.Information("Saved {Count} records to {FileName}", records.Count, fileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving data records to JSON file {FileName}", fileName);
                throw;
            }
        }

        public async Task SaveRecordsAsTsvAsync(IEnumerable<DataRecord> records, GameProfile profile, string fileName)
        {
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
                {
                    _fileSystem.CreateDirectory(directory);
                }

                if (!records.Any())
                {
                    Log.Warning("No records to save to TSV file {FileName}", fileName);
                    _fileSystem.WriteAllText(fileName, string.Empty);
                    return;
                }

                StringBuilder sb = new StringBuilder();

                // Collect all possible field names from the profile
                var allFieldNames = profile.Categories
                    .SelectMany(c => c.Fields)
                    .Select(f => f.Name)
                    .Distinct()
                    .ToList();

                // Write header row
                sb.AppendLine(string.Join("\t", allFieldNames));

                // Write data rows
                foreach (var record in records)
                {
                    var values = allFieldNames.Select(fieldName =>
                    {
                        if (record.Fields.TryGetValue(fieldName, out var value))
                        {
                            // Ensure no tabs or newlines in the output
                            return value?.ToString()?.Replace("\t", " ")?.Replace("\r", "")?.Replace("\n", "") ?? string.Empty;
                        }
                        return string.Empty;
                    });

                    sb.AppendLine(string.Join("\t", values));
                }

                _fileSystem.WriteAllText(fileName, sb.ToString());
                Log.Information("Saved {Count} records to TSV file {FileName}", records.Count(), fileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving data records to TSV file {FileName}", fileName);
                throw;
            }
        }
    }
}
