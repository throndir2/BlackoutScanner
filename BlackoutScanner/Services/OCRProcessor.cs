using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
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
        // Maximum cache sizes to prevent unbounded memory growth
        private const int MaxOCRCacheSize = 2000;
        private const int MaxImageDataCacheSize = 500; // Image data is larger, so keep fewer
        
        private Dictionary<string, TesseractEngine> tesseractEngines = new Dictionary<string, TesseractEngine>();
        private ConcurrentDictionary<string, object> engineLocks = new ConcurrentDictionary<string, object>();
        private Dictionary<string, OCRResult> ocrResultsCache = new Dictionary<string, OCRResult>();
        private Dictionary<string, string> imageDataCache = new Dictionary<string, string>();
        private readonly object _cacheLock = new object(); // Lock for cache access

        // Single static lock for ALL Tesseract operations across the application
        private static readonly object _globalTesseractLock = new object();
        private static readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);

        private bool _isInitialized = false;
        private bool _isDisposed = false;

        private readonly IImageProcessor _imageProcessor;
        private readonly IFileSystem _fileSystem;
        private readonly string _tessdataDirectory;
        private readonly ISettingsManager _settingsManager;
        private List<string> _currentLanguages = new List<string>();

        // Default constructor for DI
        public OCRProcessor(IImageProcessor imageProcessor, IFileSystem fileSystem, ISettingsManager settingsManager)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            // Default values
            _tessdataDirectory = Path.GetFullPath("tessdata"); // Use absolute path
            _currentLanguages = _settingsManager.Settings.SelectedLanguages ?? new List<string> { "eng", "kor", "jpn", "chi_sim", "chi_tra", "rus" };

            // Note: Call Initialize() after UI is shown to eagerly load Tesseract engines
            // Constructor completes quickly to allow UI to appear first
            Log.Information("OCRProcessor constructor: Created (waiting for Initialize() call)");
        }

        // For testing and specific configurations
        public OCRProcessor(IImageProcessor imageProcessor, IFileSystem fileSystem, ISettingsManager settingsManager, string[] supportedLanguages, string tessdataDirectory)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _tessdataDirectory = Path.GetFullPath(tessdataDirectory ?? throw new ArgumentNullException(nameof(tessdataDirectory)));
            _currentLanguages = supportedLanguages?.ToList() ?? throw new ArgumentNullException(nameof(supportedLanguages));

            // Note: Call Initialize() after UI is shown to eagerly load Tesseract engines
            Log.Information("OCRProcessor constructor (test): Created (waiting for Initialize() call)");
        }

        /// <summary>
        /// Public method to eagerly initialize OCR engines.
        /// Can be called from background thread after UI is shown.
        /// </summary>
        public void Initialize()
        {
            EnsureInitialized();
        }

        // Internal initialization method
        private void EnsureInitialized()
        {
            // Fast path - already initialized
            if (_isInitialized || _isDisposed) return;

            // Use semaphore to ensure only one thread initializes at a time
            _initializationSemaphore.Wait();
            try
            {
                // Double-check after acquiring semaphore
                if (_isInitialized || _isDisposed) return;

                // Initialize with global lock to prevent any concurrent Tesseract operations
                lock (_globalTesseractLock)
                {
                    // Triple-check inside lock (paranoid but safe)
                    if (_isInitialized || _isDisposed) return;

                    Log.Information("EnsureInitialized: Beginning Tesseract engine initialization");
                    InitializeTesseractEnginesInternal(_currentLanguages, _tessdataDirectory);
                    _isInitialized = true;
                    Log.Information("EnsureInitialized: Tesseract engine initialization complete");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EnsureInitialized: Failed to initialize Tesseract engines");
                throw;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }

        // Loads the OCR results cache from a dictionary.
        public void LoadCache(Dictionary<string, OCRResult> cacheData)
        {
            if (cacheData != null)
            {
                // Only load up to max size to prevent memory issues on startup
                lock (_cacheLock)
                {
                    ocrResultsCache = cacheData.Count > MaxOCRCacheSize
                        ? cacheData.TakeLast(MaxOCRCacheSize).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                        : cacheData;
                    Log.Information($"Loaded OCR cache with {ocrResultsCache.Count} entries (max: {MaxOCRCacheSize})");
                }
            }
        }

        // Exports the current state of the OCR results cache.
        public Dictionary<string, OCRResult> ExportCache()
        {
            lock (_cacheLock)
            {
                return new Dictionary<string, OCRResult>(ocrResultsCache);
            }
        }
        
        // Trims the OCR cache if it exceeds the maximum size
        private void TrimOCRCacheIfNeeded()
        {
            lock (_cacheLock)
            {
                if (ocrResultsCache.Count >= MaxOCRCacheSize)
                {
                    // Remove oldest 20% of entries
                    var keysToRemove = ocrResultsCache.Keys.Take(MaxOCRCacheSize / 5).ToList();
                    foreach (var key in keysToRemove)
                    {
                        ocrResultsCache.Remove(key);
                    }
                    Log.Debug($"OCR cache trimmed to {ocrResultsCache.Count} entries");
                }
                
                if (imageDataCache.Count >= MaxImageDataCacheSize)
                {
                    // Remove oldest 20% of entries
                    var keysToRemove = imageDataCache.Keys.Take(MaxImageDataCacheSize / 5).ToList();
                    foreach (var key in keysToRemove)
                    {
                        imageDataCache.Remove(key);
                    }
                    Log.Debug($"Image data cache trimmed to {imageDataCache.Count} entries");
                }
            }
        }
        
        // Thread-safe cache addition
        private void AddToCache(string imageHash, OCRResult result, string? imageBase64 = null)
        {
            lock (_cacheLock)
            {
                TrimOCRCacheIfNeeded();
                ocrResultsCache[imageHash] = result;
                if (imageBase64 != null)
                {
                    imageDataCache[imageHash] = imageBase64;
                }
            }
        }

        // Updates the OCR result for a given image hash with manually corrected text.
        public void UpdateCacheResult(string imageHash, string correctedText, float confidence = 100f)
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
                    // Use the provided confidence (100% for manual corrections, AI confidence for AI results)
                    updatedConfidences.Add((word, confidence));
                }

                // Update the WordConfidences with the new confidence values
                cachedResult.WordConfidences = updatedConfidences;

                // Save the updated result back to the cache
                ocrResultsCache[imageHash] = cachedResult;

                Log.Information($"Updated OCR result in cache for image hash: {imageHash} with confidence: {confidence:F2}%");
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
            // Ensure fully initialized before processing
            if (!_isInitialized)
            {
                Log.Warning($"ProcessImage called before initialization complete. Forcing initialization now.");
                EnsureInitialized();
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OCRProcessor), "Cannot process image - OCRProcessor has been disposed");
            }

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

            // Start timing
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = ProcessImageWithFallback(bitmap, attempt, numericalOnly, saveDebugImages, debugImagesFolder, verboseLogging, category, fieldName);
            stopwatch.Stop();

            // Record processing time
            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            return result;
        }

        // Process image with fallback to different OCR engines if needed
        public OCRResult ProcessImageWithFallback(Bitmap bitmap, int attempt, bool numericalOnly, bool saveDebugImages = false, string debugImagesFolder = "DebugImages", bool verboseLogging = false, string category = "", string fieldName = "")
        {
            // Ensure fully initialized before processing
            if (!_isInitialized)
            {
                Log.Warning($"ProcessImageWithFallback called before initialization complete. Forcing initialization now.");
                EnsureInitialized();
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OCRProcessor), "Cannot process image - OCRProcessor has been disposed");
            }

            Log.Debug($"[OCRProcessor.ProcessImageWithFallback] Called with saveDebugImages={saveDebugImages}, category='{category}', field='{fieldName}'");

            // Get confidence threshold from settings
            float confidenceThreshold = _settingsManager?.Settings?.OCRConfidenceThreshold ?? 80.0f;

            // Check if multi-engine mode is enabled
            bool useMultiEngine = _settingsManager?.Settings?.UseMultiEngineOCR ?? false;

            // Important: Create a memory copy of the bitmap to avoid locking the original
            Bitmap bitmapCopy;
            using (MemoryStream ms = new MemoryStream())
            {
                // Use PNG format instead of RawFormat which might be null
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                bitmapCopy = new Bitmap(ms);
            }

            BitmapImage bitmapImage = ConvertBitmapToBitmapImage(bitmapCopy);

            // Generate a hash for the bitmap
            string imageHash = GenerateImageHash(bitmapImage);

            // Check cache first - this is thread-safe and doesn't require locks
            if (ocrResultsCache.TryGetValue(imageHash, out OCRResult? cachedResult) && cachedResult != null)
            {
                if (saveDebugImages)
                {
                    Log.Debug($"[OCRProcessor.ProcessImageWithFallback] Using cached result, hash: {imageHash}");
                    SaveDebugImage(bitmapCopy, imageHash, category, fieldName, cachedResult.Text,
                        CalculateAverageConfidence(cachedResult), saveDebugImages, debugImagesFolder, verboseLogging);
                }

                // Cache the image data after retrieval
                if (!imageDataCache.ContainsKey(imageHash))
                {
                    imageDataCache[imageHash] = ConvertBitmapImageToBase64(bitmapImage);
                }

                // Dispose the copy since we're done with it
                bitmapCopy.Dispose();

                return cachedResult;
            }

            // Lock globally for ALL Tesseract operations
            lock (_globalTesseractLock)
            {
                // In single-engine mode, just process once with the combined engine
                if (!useMultiEngine)
                {
                    var combinedKey = string.Join("+", _currentLanguages);
                    if (tesseractEngines.ContainsKey(combinedKey))
                    {
                        var engine = tesseractEngines[combinedKey];

                        var result = ProcessImageWithTesseract(bitmapCopy, engine);
                        result.ImageHash = imageHash;
                        AddToCache(imageHash, result, ConvertBitmapImageToBase64(bitmapImage));

                        if (saveDebugImages)
                        {
                            SaveDebugImage(bitmapCopy, imageHash, category, fieldName, result.Text,
                                CalculateAverageConfidence(result), saveDebugImages, debugImagesFolder, verboseLogging);
                        }

                        // Dispose the copy
                        bitmapCopy.Dispose();

                        return result;
                    }
                }

                // Multi-engine processing (existing logic but within global lock)
                OCRResult? bestResult = null;
                float highestAverageConfidence = 0;

                var enginesToUse = numericalOnly ? new[] { "numerical" } : tesseractEngines.Keys.ToArray();

                foreach (var key in enginesToUse)
                {
                    if (!tesseractEngines.ContainsKey(key)) continue;

                    TesseractEngine engine = tesseractEngines[key];
                    Log.Information($"Processing image with language: {key}");

                    var currentResult = ProcessImageWithTesseract(bitmapCopy, engine);
                    currentResult.ImageHash = imageHash;

                    var allWordsMeetThreshold = currentResult.WordConfidences.All(wc => wc.Confidence >= confidenceThreshold);
                    float currentAverageConfidence = CalculateAverageConfidence(currentResult);

                    if (allWordsMeetThreshold)
                    {
                        AddToCache(imageHash, currentResult, ConvertBitmapImageToBase64(bitmapImage));

                        Log.Information($"All words met confidence threshold. Language: {key}");

                        bitmapCopy.Dispose();
                        return currentResult;
                    }
                    else if (currentAverageConfidence > highestAverageConfidence)
                    {
                        bestResult = currentResult;
                        highestAverageConfidence = currentAverageConfidence;
                    }

                    if (numericalOnly)
                    {
                        long numericalText = 0;
                        if (long.TryParse(currentResult.Text.Replace(",", "").Replace(".", ""), out numericalText))
                        {
                            Log.Information($"Text is a number.");

                            AddToCache(imageHash, currentResult, ConvertBitmapImageToBase64(bitmapImage));

                            bitmapCopy.Dispose();
                            return currentResult;
                        }
                    }
                }

                // Cache best result
                if (bestResult != null)
                {
                    AddToCache(imageHash, bestResult, ConvertBitmapImageToBase64(bitmapImage));
                }

                bitmapCopy.Dispose();
                return bestResult ?? new OCRResult { ImageHash = string.Empty, Text = string.Empty, WordConfidences = new List<(string, float)>() };
            }
        }

        // Internal initialization (called within locks)
        private void InitializeTesseractEnginesInternal(List<string> supportedLanguages, string tessdataDirectory)
        {
            try
            {
                // Ensure tessdata directory exists with absolute path
                string absoluteTessdataPath = Path.GetFullPath(tessdataDirectory);
                if (!Directory.Exists(absoluteTessdataPath))
                {
                    Directory.CreateDirectory(absoluteTessdataPath);
                }

                // Validate and download language files first (before ANY engine creation)
                ValidateAndDownloadLanguageFiles(supportedLanguages, absoluteTessdataPath);

                // Check if we should use multi-engine mode
                bool useMultiEngine = _settingsManager?.Settings?.UseMultiEngineOCR ?? false;

                if (useMultiEngine)
                {
                    Log.Information("Initializing OCR in Enhanced Accuracy mode (multiple engines)");
                    InitializeMultipleEnginesSafe(supportedLanguages, absoluteTessdataPath);
                }
                else
                {
                    Log.Information("Initializing OCR in Fast Processing mode (single combined engine)");
                    InitializeSingleCombinedEngineSafe(supportedLanguages, absoluteTessdataPath);
                }

                // Initialize a dedicated numerical engine
                InitializeNumericalEngineSafe(absoluteTessdataPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize Tesseract engines");
                // Clean up any partially initialized engines
                DisposeEnginesInternal();
                throw;
            }
        }

        private void ValidateAndDownloadLanguageFiles(List<string> languages, string tessdataDirectory)
        {
            // First, validate all files exist or download them
            var missingFiles = new List<string>();

            foreach (string lang in languages)
            {
                string trainedDataPath = Path.Combine(tessdataDirectory, $"{lang}.traineddata");
                if (!File.Exists(trainedDataPath))
                {
                    missingFiles.Add(lang);
                }
            }

            if (missingFiles.Any())
            {
                Log.Information($"Downloading missing language files: {string.Join(", ", missingFiles)}");
                DownloadLanguageFilesSafe(missingFiles, tessdataDirectory);
            }

            // Verify all files now exist
            foreach (string lang in languages)
            {
                string trainedDataPath = Path.Combine(tessdataDirectory, $"{lang}.traineddata");
                if (!File.Exists(trainedDataPath))
                {
                    throw new FileNotFoundException($"Language file not found after download attempt: {trainedDataPath}");
                }
            }
        }

        private void DownloadLanguageFilesSafe(List<string> languages, string tessdataDirectory)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                // Download files sequentially to avoid overwhelming the system
                foreach (string lang in languages)
                {
                    string trainedDataPath = Path.Combine(tessdataDirectory, $"{lang}.traineddata");
                    string tempPath = trainedDataPath + ".tmp";

                    try
                    {
                        Log.Information($"Downloading {lang}.traineddata...");

                        var downloadUrl = $"https://github.com/tesseract-ocr/tessdata/raw/main/{lang}.traineddata"; // Use standard tessdata, not tessdata_best for smaller files
                        var fileBytes = httpClient.GetByteArrayAsync(downloadUrl).Result;

                        // Write to temp file first
                        File.WriteAllBytes(tempPath, fileBytes);

                        // Then move to final location (atomic operation)
                        if (File.Exists(trainedDataPath))
                            File.Delete(trainedDataPath);
                        File.Move(tempPath, trainedDataPath);

                        Log.Information($"Successfully downloaded {lang}.traineddata");
                    }
                    catch (Exception ex)
                    {
                        // Clean up temp file
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);

                        Log.Error($"Failed to download {lang}.traineddata: {ex.Message}");
                        throw new InvalidOperationException($"Failed to download language file for '{lang}'", ex);
                    }
                }
            }
        }

        private void InitializeSingleCombinedEngineSafe(List<string> supportedLanguages, string tessdataDirectory)
        {
            string allLanguagesCombined = string.Join("+", supportedLanguages);
            Log.Information($"Initializing single combined tesseract engine: {allLanguagesCombined}");

            try
            {
                // Create engine with explicit error handling
                var engine = new TesseractEngine(tessdataDirectory, allLanguagesCombined, EngineMode.Default);

                // Verify engine is working with a simple test
                using (var testBitmap = new Bitmap(1, 1))
                using (var testPix = ConvertBitmapToPix(testBitmap))
                using (var testPage = engine.Process(testPix))
                {
                    // Just ensure it doesn't crash
                    _ = testPage.GetText();
                }

                tesseractEngines[allLanguagesCombined] = engine;
                engineLocks[allLanguagesCombined] = new object();

                Log.Information($"Successfully initialized combined engine: {allLanguagesCombined}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to initialize combined engine: {allLanguagesCombined}");
                throw;
            }
        }

        private void InitializeMultipleEnginesSafe(List<string> supportedLanguages, string tessdataDirectory)
        {
            // First initialize the combined engine
            InitializeSingleCombinedEngineSafe(supportedLanguages, tessdataDirectory);

            // Initialize individual engines one by one with proper delays
            foreach (var language in supportedLanguages)
            {
                try
                {
                    if (!tesseractEngines.ContainsKey(language))
                    {
                        Log.Information($"Initializing tesseract engine: {language}");

                        // Add delay to prevent resource conflicts
                        Thread.Sleep(500);

                        var engine = new TesseractEngine(tessdataDirectory, language, EngineMode.Default);
                        tesseractEngines[language] = engine;
                        engineLocks[language] = new object();

                        Log.Information($"Successfully initialized engine: {language}");
                    }

                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to initialize engine for language: {language}. Continuing with other languages.");
                    // Don't throw - continue with other languages
                }
            }
        }

        private void InitializeNumericalEngineSafe(string tessdataDirectory)
        {
            try
            {
                // Only initialize if eng.traineddata exists
                string engDataPath = Path.Combine(tessdataDirectory, "eng.traineddata");
                if (File.Exists(engDataPath))
                {
                    var numericalEngine = new TesseractEngine(tessdataDirectory, "eng", EngineMode.Default);
                    tesseractEngines["numerical"] = numericalEngine;
                    engineLocks["numerical"] = new object();
                    Log.Information("Successfully initialized numerical engine");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize numerical engine. Continuing without it.");
                // Non-critical, continue without numerical engine
            }
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
            if (_isDisposed) return;

            lock (_globalTesseractLock)
            {
                DisposeEnginesInternal();
                _isDisposed = true;
                _isInitialized = false;
            }
        }

        private void DisposeEnginesInternal()
        {
            foreach (var kvp in tesseractEngines)
            {
                try
                {
                    kvp.Value?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error disposing engine: {kvp.Key}");
                }
            }
            tesseractEngines.Clear();
            engineLocks.Clear();
        }

        // Add method to update languages dynamically
        public void UpdateLanguages(List<string> languages)
        {
            EnsureInitialized();

            lock (_globalTesseractLock)
            {
                Log.Information($"Updating OCR languages from [{string.Join(", ", _currentLanguages)}] to [{string.Join(", ", languages)}]");

                // Dispose all existing engines
                DisposeEnginesInternal();

                // Reinitialize with new languages
                _currentLanguages = new List<string>(languages);
                _isInitialized = false;

                // Re-initialize will happen on next use via EnsureInitialized
            }
        }

        private void RemoveLanguageEngines(string language)
        {
            Log.Information($"Removing engines for language: {language}");

            var keysToRemove = tesseractEngines.Keys
                .Where(k => k == language || k.Contains($"{language}+") || k.Contains($"+{language}"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (tesseractEngines.TryGetValue(key, out var engine))
                {
                    engine.Dispose();
                    tesseractEngines.Remove(key);
                    engineLocks.TryRemove(key, out _);
                    Log.Information($"Disposed engine: {key}");
                }
            }
        }

        private void InitializeAdditionalLanguages(List<string> languages)
        {
            // Download language files if needed
            using (var httpClient = new HttpClient())
            {
                foreach (string lang in languages)
                {
                    string trainedDataPath = Path.Combine(_tessdataDirectory, $"{lang}.traineddata");

                    if (!File.Exists(trainedDataPath))
                    {
                        Log.Information($"Downloading {lang}.traineddata...");
                        try
                        {
                            var downloadUrl = $"https://github.com/tesseract-ocr/tessdata_best/raw/main/{lang}.traineddata";
                            var fileBytes = httpClient.GetByteArrayAsync(downloadUrl).Result;
                            File.WriteAllBytes(trainedDataPath, fileBytes);
                            Log.Information($"Downloaded {lang}.traineddata");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to download {lang}.traineddata: {ex.Message}");
                            throw;
                        }
                    }
                }
            }

            // Initialize individual engines
            foreach (var language in languages)
            {
                if (!tesseractEngines.ContainsKey(language))
                {
                    Log.Information($"Initializing engine: {language}");
                    tesseractEngines[language] = new TesseractEngine(_tessdataDirectory, language, EngineMode.Default);
                    engineLocks[language] = new object();
                }

                // Initialize combinations with eng if needed
                if (language != "eng" && _currentLanguages.Contains("eng"))
                {
                    var engPlusLanguage = $"eng+{language}";
                    if (!tesseractEngines.ContainsKey(engPlusLanguage))
                    {
                        Log.Information($"Initializing engine: {engPlusLanguage}");
                        tesseractEngines[engPlusLanguage] = new TesseractEngine(_tessdataDirectory, engPlusLanguage, EngineMode.Default);
                        engineLocks[engPlusLanguage] = new object();
                    }
                }
            }
        }
    }
}
