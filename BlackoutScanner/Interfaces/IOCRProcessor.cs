using System;
using System.Collections.Generic;
using System.Drawing;
using BlackoutScanner.Models;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Interfaces
{
    public interface IOCRProcessor : IDisposable
    {
        /// <summary>
        /// Eagerly initializes OCR engines. Should be called after UI is shown.
        /// </summary>
        void Initialize();

        OCRResult ProcessImage(Bitmap image, string category = "", string fieldName = "");
        OCRResult ProcessImageWithFallback(Bitmap image, int attempt = 0, bool numericalOnly = false,
            bool saveDebugImage = false, string debugImagesFolder = "", bool verboseLogging = false,
            string category = "", string fieldName = "");
        void LoadCache(Dictionary<string, OCRResult> cacheData);
        Dictionary<string, OCRResult> ExportCache();
        void UpdateCacheResult(string imageHash, string correctedText, float confidence = 100f);
        string? GetImageData(string key);
        void SaveDebugImage(Bitmap bitmap, string category, string fieldName);
        string GenerateImageHash(BitmapImage bitmapImage);
        BitmapImage ConvertBitmapToBitmapImage(System.Drawing.Bitmap bitmap);
        void UpdateLanguages(List<string> languages);
    }
}
