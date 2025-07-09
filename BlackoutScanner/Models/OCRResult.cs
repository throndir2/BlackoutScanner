using System.Drawing;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Models
{
    public class OCRResult
    {
        public string ImageHash { get; set; }
        public string Text { get; set; }
        public List<(string Word, float Confidence)> WordConfidences { get; set; } = new List<(string Word, float Confidence)>();
    }

}
