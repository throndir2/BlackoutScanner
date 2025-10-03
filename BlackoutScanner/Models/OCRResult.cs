using System;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Models
{
    public class OCRResult
    {
        public string ImageHash { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public List<(string Word, float Confidence)> WordConfidences { get; set; } = new List<(string Word, float Confidence)>();

        /// <summary>
        /// Duration of OCR processing in milliseconds
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }

}
