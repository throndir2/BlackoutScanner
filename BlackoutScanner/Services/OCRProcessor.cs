using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Tesseract;
using Serilog;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace BlackoutScanner.Services
{
    public class OCRProcessor : IOCRProcessor
    {
        private Dictionary<string, TesseractEngine> tesseractEngines = new Dictionary<string, TesseractEngine>();
        private ConcurrentDictionary<string, object> engineLocks = new ConcurrentDictionary<string, object>();
        private Dictionary<string, OCRResult> ocrResultsCache = new Dictionary<string, OCRResult>();
        private Dictionary<string, string> imageDataCache = new Dictionary<string, string>();

        private readonly IImageProcessor _imageProcessor;
        private readonly IFileSystem _fileSystem;
        private readonly string[] _supportedLanguages;
        private readonly string _tessdataDirectory;
        private readonly ISettingsManager _settingsManager;

        // Default constructor for DI
        public OCRProcessor(IImageProcessor imageProcessor, IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            // Default values
            _supportedLanguages = new[] { "eng", "kor", "jpn", "chi_sim", "chi_tra" };
            _tessdataDirectory = "tessdata";

            InitializeTesseractEngines(_supportedLanguages, _tessdataDirectory);
        }

        // For testing and specific configurations
        public OCRProcessor(IImageProcessor imageProcessor, IFileSystem fileSystem, ISettingsManager settingsManager, string[] supportedLanguages, string tessdataDirectory)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _supportedLanguages = supportedLanguages ?? throw new ArgumentNullException(nameof(supportedLanguages));
            _tessdataDirectory = tessdataDirectory ?? throw new ArgumentNullException(nameof(tessdataDirectory));

            InitializeTesseractEngines(supportedLanguages, tessdataDirectory);
        }

        // Loads the OCR results cache from a dictionary.
        public void LoadCache(Dictionary<string, OCRResult> cacheData)
        {
            if (cacheData != null)
            {
                ocrResultsCache = cacheData;
            }
        }

        // Exports the current state of the OCR results cache.
        public Dictionary<string, OCRResult> ExportCache()
        {
            return ocrResultsCache;
        }

        // Updates the OCR result for a given image hash with manually corrected text.
        public void UpdateCacheResult(string imageHash, string correctedText)
        {
            // Check if the result is already in the cache
            if (ocrResultsCache.TryGetValue(imageHash, out OCRResult? cachedResult))
            {
                if (cachedResult == null) return;
                // Update the text of the cached result with the corrected text
                cachedResult.Text = correctedText;

                var correctedWords = correctedText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var updatedConfidences = new List<(string Word, float Confidence)>();
                foreach (var word in correctedWords)
                {
                    // Assuming 100% confidence for manually corrected words
                    updatedConfidences.Add((word, 100f));
                }

                // Save the updated result back to the cache
                ocrResultsCache[imageHash] = cachedResult;

                Log.Information($"Updated OCR result in cache for image hash: {imageHash}");
            }
        }

        // Retrieves a specific image data value from the cache.
        public string? GetImageData(string key)
        {
            if (imageDataCache.TryGetValue(key, out string? value))
            {
                return value;
            }
            else
            {
                // Log or handle the case where the key is not found as needed
                Log.Information($"No image data found for key: {key}");
                return null; // Or return a default value as appropriate
            }
        }

        // Method to save debug image information
        public void SaveDebugImage(Bitmap bitmap, string category, string fieldName)
        {
            // Get settings from SettingsManager instead of hardcoding
            bool saveDebugImages = _settingsManager?.Settings.SaveDebugImages ?? false;
            string debugImagesFolder = _settingsManager?.Settings.DebugImagesFolder ?? "DebugImages";
            string logLevel = _settingsManager?.Settings.LogLevel ?? "Information";
            bool verboseLogging = logLevel == "Verbose" || logLevel == "Debug";

            // Add detailed logging
            Log.Information($"[OCRProcessor.SaveDebugImage] Called for category='{category}', field='{fieldName}'");
            Log.Information($"[OCRProcessor.SaveDebugImage] SettingsManager is null? {_settingsManager == null}");
            if (_settingsManager != null)
            {
                Log.Information($"[OCRProcessor.SaveDebugImage] _settingsManager.Settings.SaveDebugImages={_settingsManager.Settings.SaveDebugImages}");
            }
            Log.Information($"[OCRProcessor.SaveDebugImage] saveDebugImages={saveDebugImages} (after checking settings)");

            // Don't save if the SaveDebugImages setting is off
            if (!saveDebugImages)
            {
                Log.Information($"[OCRProcessor.SaveDebugImage] NOT saving debug image because saveDebugImages is false");
                return;
            }

            Log.Information($"[OCRProcessor.SaveDebugImage] SAVING debug image because saveDebugImages is true");

            // Generate a hash for the bitmap
            BitmapImage bitmapImage = ConvertBitmapToBitmapImage(bitmap);
            string imageHash = GenerateImageHash(bitmapImage);

            // Get the OCR result if available
            string ocrResult = "";
            float confidence = 0;
            if (ocrResultsCache.TryGetValue(imageHash, out OCRResult? result) && result != null)
            {
                ocrResult = result.Text;
                confidence = CalculateAverageConfidence(result);
            }

            try
            {
                // Create a folder structure organized by date and time
                DateTime now = DateTime.Now;
                string dateFolder = Path.Combine(debugImagesFolder, now.ToString("yyyy-MM-dd"));
                string sessionFolder = Path.Combine(dateFolder, now.ToString("HH-mm-ss"));

                // Ensure the directory exists
                Directory.CreateDirectory(sessionFolder);

                // Create a filename with hash, category, field name
                string fileName = $"{now.ToString("HHmmss")}_{category}_{fieldName}_{imageHash.Substring(0, 8)}.png";
                string filePath = Path.Combine(sessionFolder, fileName);

                // Save the bitmap
                bitmap.Save(filePath);

                // Log with a link to the image file
                string message = $"OCR Debug: '{category}/{fieldName}' - Result: '{ocrResult}' (Confidence: {confidence:F2})\nImage saved: {Path.GetFullPath(filePath)}";
                Log.Information(message);

                if (verboseLogging)
                {
                    // This will log to UI via the ActionSink
                    Log.Debug("UI: " + message);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save debug image");
            }
        }

        // For internal use - keep original method with full parameters
        internal void SaveDebugImage(Bitmap bitmap, string imageHash, string category, string fieldName, string ocrResult, float confidence, bool saveDebugImages, string debugImagesFolder, bool verboseLogging)
        {
            if (!saveDebugImages) return;

            try
            {
                // Create a folder structure organized by date and time
                DateTime now = DateTime.Now;
                string dateFolder = Path.Combine(debugImagesFolder, now.ToString("yyyy-MM-dd"));
                string sessionFolder = Path.Combine(dateFolder, now.ToString("HH-mm-ss"));

                // Ensure the directory exists
                Directory.CreateDirectory(sessionFolder);

                // Create a filename with hash, category, field name
                string fileName = $"{now.ToString("HHmmss")}_{category}_{fieldName}_{imageHash.Substring(0, 8)}.png";
                string filePath = Path.Combine(sessionFolder, fileName);

                // Save the bitmap
                bitmap.Save(filePath);

                // Log with a link to the image file
                string message = $"OCR Debug: '{category}/{fieldName}' - Result: '{ocrResult}' (Confidence: {confidence:F2})\nImage saved: {Path.GetFullPath(filePath)}";
                Log.Information(message);

                if (verboseLogging)
                {
                    // This will log to UI via the ActionSink
                    Log.Debug("UI: " + message);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save debug image");
            }
        }

        public OCRResult ProcessImage(Bitmap bitmap, string category = "", string fieldName = "")
        {
            // Get values from settings instead of hardcoding
            int attempt = 0;
            bool numericalOnly = false;
            bool saveDebugImages = _settingsManager?.Settings.SaveDebugImages ?? false;
            string debugImagesFolder = _settingsManager?.Settings.DebugImagesFolder ?? "DebugImages";
            string logLevel = _settingsManager?.Settings.LogLevel ?? "Information";
            bool verboseLogging = logLevel == "Verbose" || logLevel == "Debug";

            Log.Debug($"[OCRProcessor.ProcessImage] saveDebugImages={saveDebugImages} from SettingsManager");
            if (_settingsManager != null)
            {
                Log.Debug($"[OCRProcessor.ProcessImage] _settingsManager.Settings.SaveDebugImages={_settingsManager.Settings.SaveDebugImages}");
            }

            return ProcessImageWithFallback(bitmap, attempt, numericalOnly, saveDebugImages, debugImagesFolder, verboseLogging, category, fieldName);
        }

        // Process image with fallback to different OCR engines if needed
        public OCRResult ProcessImageWithFallback(Bitmap bitmap, int attempt, bool numericalOnly, bool saveDebugImages = false, string debugImagesFolder = "DebugImages", bool verboseLogging = false, string category = "", string fieldName = "")
        {
            Log.Debug($"[OCRProcessor.ProcessImageWithFallback] Called with saveDebugImages={saveDebugImages}, category='{category}', field='{fieldName}'");

            // Get confidence threshold from settings
            float confidenceThreshold = _settingsManager?.Settings?.OCRConfidenceThreshold ?? 80.0f;

            // Check if multi-engine mode is enabled
            bool useMultiEngine = _settingsManager?.Settings?.UseMultiEngineOCR ?? false;

            BitmapImage bitmapImage = ConvertBitmapToBitmapImage(bitmap);

            // Generate a hash for the bitmap
            string imageHash = GenerateImageHash(bitmapImage);

            // Save imageData for later retrieval to UI
            this.imageDataCache[imageHash] = ConvertBitmapImageToBase64(bitmapImage);

            // Check if the result is already in the cache
            if (ocrResultsCache.TryGetValue(imageHash, out OCRResult? cachedResult))
            {
                // If cache is null, then we should attempt to process this anyways
                if (cachedResult != null)
                {
                    if (saveDebugImages)
                    {
                        Log.Debug($"[OCRProcessor.ProcessImageWithFallback] Calling SaveDebugImage for cached result because saveDebugImages={saveDebugImages}");
                        // Save a debug image with cached result
                        SaveDebugImage(bitmap, imageHash, category, fieldName, cachedResult.Text,
                            CalculateAverageConfidence(cachedResult), saveDebugImages, debugImagesFolder, verboseLogging);
                    }
                    else
                    {
                        Log.Debug($"[OCRProcessor.ProcessImageWithFallback] NOT calling SaveDebugImage for cached result because saveDebugImages={saveDebugImages}");
                    }
                    return cachedResult;
                }
            }

            // In single-engine mode, just process once with the combined engine
            if (!useMultiEngine)
            {
                var combinedKey = string.Join("+", _supportedLanguages);
                if (tesseractEngines.ContainsKey(combinedKey))
                {
                    var engine = tesseractEngines[combinedKey];
                    var lockObj = engineLocks[combinedKey];

                    lock (lockObj)
                    {
                        var result = ProcessImageWithTesseract(bitmap, engine);
                        result.ImageHash = imageHash;
                        ocrResultsCache[imageHash] = result;

                        if (saveDebugImages)
                        {
                            SaveDebugImage(bitmap, imageHash, category, fieldName, result.Text,
                                CalculateAverageConfidence(result), saveDebugImages, debugImagesFolder, verboseLogging);
                        }

                        return result;
                    }
                }
            }

            // If we're using multi-engine mode or the single combined engine isn't available,
            // fall back to the original multiple-engine logic
            OCRResult? bestResult = null;
            float highestAverageConfidence = 0;

            // With some numbers, the eng model isn't able to parse the value.
            var enginesToUse = numericalOnly ? new[] { "numerical" } : tesseractEngines.Keys.ToArray();

            foreach (var key in enginesToUse)
            {
                TesseractEngine engine = tesseractEngines[key];
                object lockObj = engineLocks[key];

                lock (lockObj) // Serialize access to the TesseractEngine
                {
                    Log.Information($"Processing image with language: {key}");

                    var currentResult = ProcessImageWithTesseract(bitmap, engine);

                    currentResult.ImageHash = imageHash;

                    var allWordsMeetThreshold = currentResult.WordConfidences.All(wc => wc.Confidence >= confidenceThreshold);
                    float currentAverageConfidence = CalculateAverageConfidence(currentResult);

                    if (allWordsMeetThreshold)
                    {
                        ocrResultsCache[imageHash] = currentResult;
                        Log.Information($"All words met confidence threshold. Language: {key}");
                        return currentResult;
                    }
                    else if (currentAverageConfidence > highestAverageConfidence)
                    {
                        bestResult = currentResult;
                        highestAverageConfidence = currentAverageConfidence;
                    }

                    // If OCR mode is set to numerical, this will have to be some sort of number.
                    // In that case, let's try to convert it to a number, and if so, it's a good result.
                    if (numericalOnly)
                    {
                        long numericalText = 0;
                        if (long.TryParse(currentResult.Text.Replace(",", "").Replace(".", ""), out numericalText))
                        {
                            Log.Information($"Text is a number.");
                            return currentResult;
                        }
                    }
                }
            }

            // If we reach here, it means no language engine resulted in all words meeting the confidence threshold
            Log.Information($"Falling back to best result based on highest average confidence.");

            // If a new result is computed, cache it
            if (bestResult != null)
            {
                ocrResultsCache[imageHash] = bestResult;
            }

            return bestResult ?? new OCRResult { ImageHash = string.Empty, Text = string.Empty, WordConfidences = new List<(string, float)>() };
        }

        // Initialize Tesseract engines with supported languages
        private void InitializeTesseractEngines(string[] supportedLanguages, string tessdataDirectory)
        {
            if (!Directory.Exists(tessdataDirectory))
            {
                Directory.CreateDirectory(tessdataDirectory);
            }

            using (var httpClient = new HttpClient())
            {
                foreach (string lang in supportedLanguages)
                {
                    string trainedDataPath = Path.Combine(tessdataDirectory, $"{lang}.traineddata");

                    if (!File.Exists(trainedDataPath))
                    {
                        Log.Information($"{lang}.traineddata file not found. Starting download...");
                        try
                        {
                            var downloadUrl = $"https://github.com/tesseract-ocr/tessdata_best/raw/main/{lang}.traineddata";
                            var fileBytes = httpClient.GetByteArrayAsync(downloadUrl).Result; // Synchronous download
                            File.WriteAllBytes(trainedDataPath, fileBytes);
                            Log.Information("Download completed for " + lang);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to download {lang}.traineddata: {ex.Message}");
                            // Propagate the exception to be caught by the caller
                            throw new InvalidOperationException($"Failed to initialize OCR engine for language '{lang}' due to download failure.", ex);
                        }
                    }
                    else
                    {
                        Log.Information($"{lang}.traineddata file already exists. No download needed for " + lang);
                    }
                }
            }

            // Check if we should use multi-engine mode
            bool useMultiEngine = _settingsManager?.Settings?.UseMultiEngineOCR ?? false;

            if (useMultiEngine)
            {
                Log.Information("Initializing OCR in Enhanced Accuracy mode (multiple engines)");
            }
            else
            {
                Log.Information("Initializing OCR in Fast Processing mode (single combined engine)");
            }

            // Initialize a combined engine with all languages
            string allLanguagesCombined = string.Join("+", supportedLanguages);
            Log.Information($"Initializing tesseract engine: {allLanguagesCombined}");
            tesseractEngines[allLanguagesCombined] = new TesseractEngine(tessdataDirectory, allLanguagesCombined, EngineMode.Default);
            engineLocks[allLanguagesCombined] = new object();

            // Initialize engines for "eng" with each of the other languages
            foreach (var language in supportedLanguages)
            {
                if (!tesseractEngines.ContainsKey(language))
                {
                    Log.Information($"Initializing tesseract engine: {language}");
                    tesseractEngines[language] = new TesseractEngine(tessdataDirectory, language, EngineMode.Default);

                    // Initialize lock object for each engine
                    engineLocks[language] = new object();
                }

                if (language != "eng")
                {
                    // "eng" + other language
                    string engPlusLanguage = $"eng+{language}";
                    if (!tesseractEngines.ContainsKey(engPlusLanguage))
                    {
                        Log.Information($"Initializing tesseract engine: {engPlusLanguage}");
                        tesseractEngines[engPlusLanguage] = new TesseractEngine(tessdataDirectory, engPlusLanguage, EngineMode.Default);
                        engineLocks[engPlusLanguage] = new object();
                    }

                    // Other language + "eng"
                    string languagePlusEng = $"{language}+eng";
                    if (!tesseractEngines.ContainsKey(languagePlusEng))
                    {
                        Log.Information($"Initializing tesseract engine: {languagePlusEng}");
                        tesseractEngines[languagePlusEng] = new TesseractEngine(tessdataDirectory, languagePlusEng, EngineMode.Default);
                        engineLocks[languagePlusEng] = new object();
                    }
                }
            }

            // Initialize a dedicated numerical engine
            var numericalEngine = new TesseractEngine(tessdataDirectory, "eng", EngineMode.Default);
            //numericalEngine.SetVariable("tessedit_char_whitelist", "0123456789,.");
            tesseractEngines["numerical"] = numericalEngine;
            engineLocks["numerical"] = new object();
        }

        private OCRResult ProcessImageWithTesseract(Bitmap bitmap, TesseractEngine engine)
        {
            using (var img = ConvertBitmapToPix(bitmap))
            {
                return PerformOCRAndPrintConfidence(img, engine);
            }
        }

        private Pix ConvertBitmapToPix(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Tiff);
                // bitmap.Dispose(); // Ensure resources are freed
                ms.Position = 0;
                return Pix.LoadTiffFromMemory(ms.ToArray());
            }
        }

        private OCRResult PerformOCRAndPrintConfidence(Pix img, TesseractEngine engine)
        {
            using (var page = engine.Process(img, PageSegMode.SingleLine))
            {
                var result = new OCRResult
                {
                    Text = page.GetText().Trim(),
                    WordConfidences = ExtractWordConfidences(page)
                };
                return result;
            }
        }

        private List<(string Word, float Confidence)> ExtractWordConfidences(Tesseract.Page page)
        {
            var wordConfidences = new List<(string, float)>();
            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    if (iter.IsAtBeginningOf(PageIteratorLevel.Word))
                    {
                        string word = iter.GetText(PageIteratorLevel.Word).Trim();
                        float confidence = iter.GetConfidence(PageIteratorLevel.Word);
                        Log.Information($"Word: {word} | Confidence score: {confidence}");
                        wordConfidences.Add((word, confidence));
                    }
                } while (iter.Next(PageIteratorLevel.Word));
            }
            return wordConfidences;
        }

        private float CalculateAverageConfidence(OCRResult result)
        {
            if (result.WordConfidences.Count == 0) return 0;
            return (float)result.WordConfidences.Average(wc => wc.Confidence);
        }

        public string GenerateImageHash(BitmapImage bitmapImage)
        {
            byte[] data;
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                data = stream.ToArray();
            }

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                return Convert.ToBase64String(hash);
            }
        }

        public BitmapImage ConvertBitmapToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memory;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Optional, makes the image usable across threads
                return bitmapImage;
            }
        }

        private string ConvertBitmapImageToBase64(BitmapImage bitmapImage)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                encoder.Save(ms);
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        public BitmapImage ConvertBase64ToBitmapImage(string base64String)
        {
            byte[] imageBytes = Convert.FromBase64String(base64String);
            using (MemoryStream ms = new MemoryStream(imageBytes))
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = ms;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Ensure it's usable across threads
                return bitmapImage;
            }
        }

        // Dispose method to clean up Tesseract engines
        public void Dispose()
        {
            DisposeEngines();
        }

        private void DisposeEngines()
        {
            foreach (var engine in tesseractEngines.Values)
            {
                engine.Dispose();
            }
            tesseractEngines.Clear();
        }
    }
}
