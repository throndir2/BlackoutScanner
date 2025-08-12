using System;
using System.Collections.Generic;

namespace BlackoutScanner.Models
{
    public class DataRecordGroup
    {
        public Guid GroupId { get; set; }
        public DateTime Timestamp { get; set; }
        public List<DataRecord> Records { get; set; } = new List<DataRecord>();
    }
}
