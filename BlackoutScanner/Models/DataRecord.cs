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

        public string ToDisplayString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Profile: {GameProfile}");
            sb.AppendLine($"Category: {Category}");
            foreach (var field in Fields)
            {
                sb.AppendLine($"{field.Key}: {field.Value}");
            }
            sb.AppendLine($"Scan Date: {ScanDate:g}");
            return sb.ToString();
        }
    }
}
