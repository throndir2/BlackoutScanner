using System;
using System.Collections.Generic;
using System.Text;

namespace BlackoutScanner.Models
{
    public class DataRecord
    {
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
        public DateTime ScanDate { get; set; }
        public string Category { get; set; } = string.Empty;
        public string GameProfile { get; set; } = string.Empty;

        // Multi-entity support properties
        public int? EntityIndex { get; set; }
        public Guid? GroupId { get; set; } // Groups multiple entities from same scan

        public string ToDisplayString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Profile: {GameProfile}");
            sb.AppendLine($"Category: {Category}");

            if (EntityIndex.HasValue)
            {
                sb.AppendLine($"Row: {EntityIndex.Value + 1}");
            }

            foreach (var field in Fields)
            {
                sb.AppendLine($"{field.Key}: {field.Value}");
            }
            sb.AppendLine($"Scan Date: {ScanDate:g}");
            return sb.ToString();
        }
    }
}
