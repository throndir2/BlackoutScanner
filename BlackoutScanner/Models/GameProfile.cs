using System.Collections.Generic;

namespace BlackoutScanner.Models
{
    public class GameProfile
    {
        public string ProfileName { get; set; } = "";
        public string GameWindowTitle { get; set; } = "";
        public List<CaptureCategory> Categories { get; set; } = new List<CaptureCategory>();
    }
}
