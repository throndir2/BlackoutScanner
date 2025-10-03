using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Services
{
    public class Scanner : IScanner
    {
        private bool continueScanning = false;
        private readonly IDataManager dataManager;
        private readonly IOCRProcessor ocrProcessor;
        private readonly IScreenCapture screenCapture;
        private readonly IAIQueueProcessor? aiQueueProcessor;
        private readonly ISettingsManager? settingsManager;
        private GameProfile? activeProfile;

        // Debug settings
        private bool saveDebugImages;
        private bool verboseLogging;
        private string debugImagesFolder;

        private readonly object bitmapLock = new object();

        // Change detection - track previous hashes to avoid unnecessary OCR
        private Dictionary<string, string> previousCategoryHashes = new Dictionary<string, string>();
        private Dictionary<string, string> previousFieldHashes = new Dictionary<string, string>();

        public event Action<Dictionary<string, object>>? DataUpdated;
        public event Action<string, BitmapImage>? ImageUpdated;
        public event Action<DateTime>? ScanDateUpdated;
        public event Action<string>? CategoryScanning; // Add this new event

        public Scanner(IDataManager dataManager, IOCRProcessor ocrProcessor, IScreenCapture screenCapture, IAIQueueProcessor? aiQueueProcessor = null, ISettingsManager? settingsManager = null)
        {
            this.dataManager = dataManager;
            this.ocrProcessor = ocrProcessor;
            this.screenCapture = screenCapture;
            this.aiQueueProcessor = aiQueueProcessor;
            this.settingsManager = settingsManager;

            // Default debug settings
            this.saveDebugImages = false;
            this.verboseLogging = false;
            this.debugImagesFolder = "DebugImages";

            // Log initial values
            Log.Information("[Scanner.Constructor] Scanner initialized with default settings: " +
                           $"saveDebugImages={this.saveDebugImages}, " +
                           $"verboseLogging={this.verboseLogging}, " +
                           $"debugImagesFolder={this.debugImagesFolder}");

            // Log AI Queue Processor availability
            if (this.aiQueueProcessor != null)
            {
                Log.Information("[Scanner.Constructor] AIQueueProcessor is AVAILABLE and will be used for low-confidence results");
            }
            else
            {
                Log.Warning("[Scanner.Constructor] AIQueueProcessor is NULL - low-confidence results will NOT be queued for AI enhancement");
            }

            if (this.settingsManager != null)
            {
                Log.Information("[Scanner.Constructor] SettingsManager is AVAILABLE");
            }
            else
            {
                Log.Warning("[Scanner.Constructor] SettingsManager is NULL - cannot check AI enhancement settings");
            }
        }

        public void UpdateDebugSettings(bool saveDebugImages, bool verboseLogging, string debugImagesFolder)
        {
            Log.Information($"[Scanner.UpdateDebugSettings] Called with saveDebugImages={saveDebugImages}, verboseLogging={verboseLogging}, folder={debugImagesFolder}");
            Log.Information($"[Scanner.UpdateDebugSettings] Previous values: saveDebugImages={this.saveDebugImages}, verboseLogging={this.verboseLogging}");

            this.saveDebugImages = saveDebugImages;
            this.verboseLogging = verboseLogging;
            this.debugImagesFolder = debugImagesFolder;

            Log.Information($"[Scanner.UpdateDebugSettings] New values set: saveDebugImages={this.saveDebugImages}, verboseLogging={this.verboseLogging}");
        }

        public void StartScanning(GameProfile profile)
        {
            activeProfile = profile;
            continueScanning = true;

            // Clear previous hashes when starting a new scan to ensure fresh detection
            previousCategoryHashes.Clear();
            previousFieldHashes.Clear();
            Log.Debug("[Scanner.StartScanning] Cleared previous hash caches for change detection");

            Task.Run(() =>
            {
                screenCapture.BringGameWindowToFront(activeProfile.GameWindowTitle);
                Thread.Sleep(1000); // Wait for the window to be brought to the front

                while (continueScanning)
                {
                    try
                    {
                        PerformScreenCaptureAndOCR();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "An error occurred during the scanning loop.");
                    }
                    Thread.Sleep(50); // Adjust delay as needed
                }
            });
        }

        public void StopScanning()
        {
            continueScanning = false;
        }

        private void PerformScreenCaptureAndOCR()
        {
            if (activeProfile == null) return;

            Rectangle gameWindowRect = screenCapture.GetClientRectangle(activeProfile.GameWindowTitle);
            if (gameWindowRect == Rectangle.Empty) return;

            // Create a container rectangle for coordinate conversion
            var containerRect = new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height);

            using (Bitmap gameWindowBitmap = screenCapture.CaptureScreenArea(gameWindowRect))
            {
                foreach (var category in activeProfile.Categories)
                {
                    // Convert relative bounds to absolute for this window size
                    var categoryAbsoluteBounds = category.RelativeBounds.ToAbsolute(containerRect);

                    // First check if category area has changed to avoid unnecessary processing
                    string categoryHash;
                    using (var categoryBitmap = gameWindowBitmap.Clone(categoryAbsoluteBounds, gameWindowBitmap.PixelFormat))
                    {
                        categoryHash = ocrProcessor.GenerateImageHash(
                            ocrProcessor.ConvertBitmapToBitmapImage(categoryBitmap)
                        );

                        if (previousCategoryHashes.TryGetValue(category.Name, out var prevHash) &&
                            prevHash == categoryHash)
                        {
                            // Category hasn't changed, skip processing
                            Log.Debug($"Scanner: Category '{category.Name}' unchanged (hash match), skipping processing");
                            continue;
                        }

                        // Store the new hash for next comparison
                        previousCategoryHashes[category.Name] = categoryHash;

                        // CRITICAL: Clear field-level hashes for this category to prevent race conditions
                        // when rapidly switching between different entities (e.g., different players)
                        var keysToRemove = previousFieldHashes.Keys
                            .Where(k => k.StartsWith($"{category.Name}_"))
                            .ToList();
                        foreach (var key in keysToRemove)
                        {
                            previousFieldHashes.Remove(key);
                        }
                        if (keysToRemove.Count > 0)
                        {
                            Log.Debug($"Scanner: Category '{category.Name}' changed - cleared {keysToRemove.Count} field hash(es) to prevent cross-entity data contamination");
                        }
                    }

                    // Check if this category matches based on its comparison mode
                    bool isCategoryMatch = false;

                    if (category.ComparisonMode == CategoryComparisonMode.Text)
                    {
                        // Text comparison mode - use OCR
                        OCRResult categoryResult = ProcessArea(gameWindowBitmap, categoryAbsoluteBounds, false, "CategoryHeader", category.Name);
                        isCategoryMatch = !string.IsNullOrWhiteSpace(category.TextToCompare) &&
                                          categoryResult.Text.Trim().Contains(category.TextToCompare, StringComparison.OrdinalIgnoreCase);
                    }
                    else // Image comparison mode
                    {
                        // Image comparison mode
                        if (category.PreviewImageData != null && category.PreviewImageData.Length > 0)
                        {
                            using (var categoryAreaBitmap = gameWindowBitmap.Clone(categoryAbsoluteBounds, gameWindowBitmap.PixelFormat))
                            {
                                using (var ms = new System.IO.MemoryStream(category.PreviewImageData))
                                using (var referenceImage = new Bitmap(ms))
                                {
                                    isCategoryMatch = CompareImages(categoryAreaBitmap, referenceImage);
                                }
                            }
                        }
                    }

                    if (isCategoryMatch)
                    {
                        // Notify that we're scanning this category
                        CategoryScanning?.Invoke(category.Name);

                        if (category.IsMultiEntity)
                        {
                            ProcessMultiEntityCategory(category, gameWindowBitmap, containerRect);
                        }
                        else
                        {
                            ProcessSingleEntityCategory(category, gameWindowBitmap, containerRect);
                        }
                    }
                }
            }
        }

        private void ProcessMultiEntityCategory(CaptureCategory category, Bitmap gameWindowBitmap, Rectangle containerRect)
        {
            if (activeProfile == null) return;

            // Create a group ID for this scan session
            var groupId = Guid.NewGuid();
            var scanTime = DateTime.UtcNow;

            // Use ConcurrentBag to safely collect results from parallel processing
            var results = new System.Collections.Concurrent.ConcurrentBag<(int entityIndex, Dictionary<string, object> fields, Dictionary<string, float> confidences, bool hasValidData)>();

            // Process entities in parallel
            Parallel.For(0, category.MaxEntityCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, entityIndex =>
            {
                var offsetY = entityIndex * category.EntityHeightOffset;
                var updatedFields = new Dictionary<string, object>();
                var fieldConfidences = new Dictionary<string, float>();
                bool hasValidData = false;

                foreach (var field in category.Fields)
                {
                    // First convert the field's relative bounds to absolute
                    var fieldAbsoluteBounds = field.RelativeBounds.ToAbsolute(containerRect);

                    // Then apply the pixel offset to the absolute bounds
                    var offsetFieldBounds = new Rectangle(
                        fieldAbsoluteBounds.X,
                        fieldAbsoluteBounds.Y + offsetY,  // Now we're adding pixels to pixels
                        fieldAbsoluteBounds.Width,
                        fieldAbsoluteBounds.Height
                    );

                    // Check if the field is still within screen bounds
                    if (offsetFieldBounds.Y + offsetFieldBounds.Height > containerRect.Height)
                    {
                        Log.Debug($"Entity {entityIndex} field {field.Name} exceeds screen bounds, stopping scan");
                        break;
                    }

                    // Check if this specific field has changed before performing OCR
                    string fieldKey = $"{category.Name}_{field.Name}_row{entityIndex}";
                    string currentFieldHash;
                    float avgConfidence = 0f; // Declare once at loop level

                    using (var fieldBitmap = gameWindowBitmap.Clone(offsetFieldBounds, gameWindowBitmap.PixelFormat))
                    {
                        currentFieldHash = ocrProcessor.GenerateImageHash(
                            ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap)
                        );

                        // Check if this field has changed
                        if (previousFieldHashes.TryGetValue(fieldKey, out var prevFieldHash) &&
                            prevFieldHash == currentFieldHash)
                        {
                            Log.Debug($"Scanner: Multi-entity field '{field.Name}' row {entityIndex} unchanged (hash match), retrieving cached OCR result");

                            // Get cached OCR result to retrieve both text and confidence
                            OCRResult cachedResult = ocrProcessor.ProcessImage(fieldBitmap, category.Name, $"{field.Name}_row{entityIndex}");
                            updatedFields[field.Name] = cachedResult.Text;

                            // Calculate and store average confidence from cached result
                            avgConfidence = cachedResult.WordConfidences.Any()
                                ? (float)cachedResult.WordConfidences.Average(wc => wc.Confidence)
                                : 0f;
                            fieldConfidences[field.Name] = avgConfidence;

                            // Check if this field has any data
                            if (!string.IsNullOrWhiteSpace(cachedResult.Text))
                            {
                                hasValidData = true;
                            }

                            continue;
                        }

                        // Store the new hash
                        previousFieldHashes[fieldKey] = currentFieldHash;
                        Log.Debug($"Scanner: Multi-entity field '{field.Name}' row {entityIndex} changed (new hash), performing OCR");
                    }

                    OCRResult fieldResult = ProcessArea(gameWindowBitmap, offsetFieldBounds, false, category.Name, $"{field.Name}_row{entityIndex}");
                    updatedFields[field.Name] = fieldResult.Text;

                    // Calculate and store average confidence for this field
                    avgConfidence = fieldResult.WordConfidences.Any()
                        ? (float)fieldResult.WordConfidences.Average(wc => wc.Confidence)
                        : 0f;
                    fieldConfidences[field.Name] = avgConfidence;

                    // Check if this field has any data
                    if (!string.IsNullOrWhiteSpace(fieldResult.Text))
                    {
                        hasValidData = true;
                    }

                    using (var fieldBitmap = gameWindowBitmap.Clone(offsetFieldBounds, gameWindowBitmap.PixelFormat))
                    {
                        // Use the entity index in the field name for multi-entity display
                        ImageUpdated?.Invoke($"{field.Name} (Row {entityIndex + 1})", ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap));
                    }
                }

                // Add the result to our concurrent collection
                results.Add((entityIndex, updatedFields, fieldConfidences, hasValidData));
            });

            // Process results sequentially to ensure consistent data handling
            foreach (var result in results.OrderBy(r => r.entityIndex))
            {
                if (result.hasValidData)
                {
                    var dataRecord = new DataRecord
                    {
                        Fields = new Dictionary<string, object>(result.fields),
                        FieldConfidences = result.confidences,
                        ScanDate = scanTime,
                        Category = category.Name,
                        GameProfile = activeProfile.ProfileName,
                        EntityIndex = result.entityIndex,
                        GroupId = groupId
                    };

                    // IMPORTANT: Include the category name in the data being sent
                    var updateFields = new Dictionary<string, object>(result.fields)
                    {
                        ["__Category__"] = category.Name
                    };

                    DataUpdated?.Invoke(updateFields);
                    ScanDateUpdated?.Invoke(dataRecord.ScanDate);

                    dataManager.AddOrUpdateRecord(dataRecord, activeProfile);
                }
                else
                {
                    // If we find an empty row, we might want to stop scanning
                    // This is optional based on requirements
                    Log.Debug($"Entity {result.entityIndex} appears empty, continuing to next entity");
                }
            }
        }

        private void ProcessSingleEntityCategory(CaptureCategory category, Bitmap gameWindowBitmap, Rectangle containerRect)
        {
            if (activeProfile == null) return;

            Log.Debug($"Processing single entity category: {category.Name}");

            var dataRecord = new DataRecord();
            var updatedFields = new Dictionary<string, object>();
            var fieldConfidences = new Dictionary<string, float>();

            // Track fields that need AI enhancement and their metadata
            var aiEnhancementQueue = new List<(string fieldName, OCRResult fieldResult, Rectangle bounds, float avgConfidence)>();

            foreach (var field in category.Fields)
            {
                // Convert relative bounds to absolute for this window size
                var fieldAbsoluteBounds = field.RelativeBounds.ToAbsolute(containerRect);

                // Check if this specific field has changed before performing OCR
                string fieldKey = $"{category.Name}_{field.Name}";
                string currentFieldHash;
                float avgConfidence = 0f; // Declare once at loop level

                using (var fieldBitmap = gameWindowBitmap.Clone(fieldAbsoluteBounds, gameWindowBitmap.PixelFormat))
                {
                    currentFieldHash = ocrProcessor.GenerateImageHash(
                        ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap)
                    );

                    // Check if this field has changed
                    if (previousFieldHashes.TryGetValue(fieldKey, out var prevFieldHash) &&
                        prevFieldHash == currentFieldHash)
                    {
                        // Field hasn't changed, retrieve cached OCR result for confidence
                        Log.Debug($"Scanner: Field '{field.Name}' unchanged (hash match), retrieving cached OCR result");

                        // Get cached OCR result to retrieve both text and confidence
                        OCRResult cachedResult = ocrProcessor.ProcessImage(fieldBitmap, category.Name, field.Name);
                        updatedFields[field.Name] = cachedResult.Text;

                        // Calculate and store average confidence from cached result
                        avgConfidence = cachedResult.WordConfidences.Any()
                            ? (float)cachedResult.WordConfidences.Average(wc => wc.Confidence)
                            : 0f;
                        fieldConfidences[field.Name] = avgConfidence;

                        Log.Debug($"Field '{field.Name}' cached result: '{cachedResult.Text}' (Confidence: {avgConfidence:F2})");

                        continue;
                    }

                    // Store the new hash
                    previousFieldHashes[fieldKey] = currentFieldHash;
                    Log.Debug($"Scanner: Field '{field.Name}' changed (new hash), performing OCR");
                }

                OCRResult fieldResult = ProcessArea(gameWindowBitmap, fieldAbsoluteBounds, false, category.Name, field.Name);
                updatedFields[field.Name] = fieldResult.Text;

                // Calculate and store average confidence for this field
                avgConfidence = fieldResult.WordConfidences.Any()
                    ? (float)fieldResult.WordConfidences.Average(wc => wc.Confidence)
                    : 0f;
                fieldConfidences[field.Name] = avgConfidence;

                Log.Debug($"Field '{field.Name}' OCR result: '{fieldResult.Text}' (Confidence: {avgConfidence:F2}, IsKeyField: {field.IsKeyField})");

                // Check if we should enqueue for AI enhancement LATER (after record creation)
                if (settingsManager != null && aiQueueProcessor != null)
                {
                    bool useAIEnhancement = settingsManager.Settings.UseAIEnhancedOCR;
                    float confidenceThreshold = settingsManager.Settings.OCRConfidenceThreshold;

                    Log.Debug($"[Scanner] AI Enhancement enabled: {useAIEnhancement}, Confidence threshold: {confidenceThreshold}, Current confidence: {avgConfidence:F2}");

                    if (useAIEnhancement && avgConfidence < confidenceThreshold)
                    {
                        // Queue this field for AI enhancement AFTER we create the record
                        aiEnhancementQueue.Add((field.Name, fieldResult, fieldAbsoluteBounds, avgConfidence));
                    }
                    else if (!useAIEnhancement)
                    {
                        Log.Debug($"[Scanner] AI Enhancement is disabled, skipping queue for '{field.Name}'");
                    }
                    else
                    {
                        Log.Debug($"[Scanner] Confidence {avgConfidence:F2} >= threshold {confidenceThreshold}, skipping AI queue for '{field.Name}'");
                    }
                }
                else
                {
                    if (settingsManager == null)
                        Log.Debug($"[Scanner] SettingsManager is null, cannot check AI enhancement settings");
                    if (aiQueueProcessor == null)
                        Log.Debug($"[Scanner] AIQueueProcessor is null, cannot enqueue items");
                }

                using (var fieldBitmap = gameWindowBitmap.Clone(fieldAbsoluteBounds, gameWindowBitmap.PixelFormat))
                {
                    ImageUpdated?.Invoke(field.Name, ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap));
                }
            }

            // IMPORTANT: Copy the updatedFields to dataRecord.Fields
            dataRecord.Fields = new Dictionary<string, object>(updatedFields);
            dataRecord.FieldConfidences = fieldConfidences;
            dataRecord.ScanDate = DateTime.UtcNow;
            dataRecord.Category = category.Name; // Add category name
            dataRecord.GameProfile = activeProfile.ProfileName; // Add profile name
            dataRecord.EntityIndex = null; // Not a multi-entity scan
            dataRecord.GroupId = null;

            // Log the data record details before saving
            Log.Debug($"DataRecord created - Category: {dataRecord.Category}, Profile: {dataRecord.GameProfile}");
            Log.Debug($"DataRecord fields: {string.Join(", ", dataRecord.Fields.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            Log.Debug($"DataRecord confidence scores: {string.Join(", ", dataRecord.FieldConfidences.Select(kvp => $"{kvp.Key}={kvp.Value:F2}"))}");

            // Log key fields for debugging
            var keyFields = category.Fields.Where(f => f.IsKeyField).Select(f => f.Name).ToList();
            if (keyFields.Any())
            {
                Log.Debug($"Key fields for category '{category.Name}': {string.Join(", ", keyFields)}");
                foreach (var keyField in keyFields)
                {
                    if (dataRecord.Fields.TryGetValue(keyField, out var value))
                    {
                        Log.Debug($"Key field '{keyField}' value: '{value}'");
                    }
                    else
                    {
                        Log.Warning($"Key field '{keyField}' not found in captured data!");
                    }
                }
            }
            else
            {
                Log.Warning($"No key fields defined for category '{category.Name}'");
            }

            // IMPORTANT: Include the category name in the data being sent
            updatedFields["__Category__"] = category.Name;

            DataUpdated?.Invoke(updatedFields);
            ScanDateUpdated?.Invoke(dataRecord.ScanDate);

            // Save the record and get its hash
            dataManager.AddOrUpdateRecord(dataRecord, activeProfile);

            // NOW enqueue low-confidence fields for AI enhancement WITH the record hash
            // This ensures we know which specific record these fields belong to
            if (aiEnhancementQueue.Any())
            {
                // Generate the hash for this specific record
                var recordHash = dataManager.GenerateDataHash(dataRecord, activeProfile);
                Log.Debug($"[Scanner] Record hash generated: {recordHash}, enqueueing {aiEnhancementQueue.Count} field(s) for AI enhancement");

                foreach (var (fieldName, fieldResult, bounds, avgConfidence) in aiEnhancementQueue)
                {
                    Log.Information($"[Scanner] ENQUEUING low-confidence OCR result for AI enhancement: Category='{category.Name}', Field='{fieldName}', Text='{fieldResult.Text}', Confidence={avgConfidence:F2}, TesseractTime={fieldResult.ProcessingTimeMs}ms, RecordHash='{recordHash}'");

                    try
                    {
                        // Capture the field image as byte array
                        using (var fieldBitmap = gameWindowBitmap.Clone(bounds, gameWindowBitmap.PixelFormat))
                        using (var ms = new System.IO.MemoryStream())
                        {
                            fieldBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            var imageData = ms.ToArray();

                            // Generate image hash for cache lookup
                            var bitmapImage = ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap);
                            var imageHash = ocrProcessor.GenerateImageHash(bitmapImage);

                            var queueItem = new AIOCRQueueItem
                            {
                                ImageData = imageData,
                                OriginalResult = fieldResult,
                                CategoryName = category.Name,
                                FieldName = fieldName,
                                ImageHash = imageHash,
                                RecordHash = recordHash, // CRITICAL: Associate with specific record
                                QueuedAt = DateTime.UtcNow
                            };

                            Log.Debug($"[Scanner] Queue item created with OriginalResult.ProcessingTimeMs={queueItem.OriginalResult.ProcessingTimeMs}ms, ImageHash={imageHash}, RecordHash={recordHash}");

                            aiQueueProcessor.Enqueue(queueItem);
                            Log.Information($"[Scanner] Successfully enqueued item to AI queue. Current queue size: {aiQueueProcessor.QueueCount}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[Scanner] Failed to enqueue item to AI queue for field '{fieldName}'");
                    }
                }
            }
        }

        private OCRResult ProcessArea(Bitmap gameWindowBitmap, Rectangle area, bool numericalOnly = false, string category = "", string fieldName = "")
        {
            try
            {
                Log.Debug($"[Scanner.ProcessArea] Processing area for category='{category}', field='{fieldName}'");
                Log.Debug($"[Scanner.ProcessArea] Current scanner settings: saveDebugImages={saveDebugImages}, verboseLogging={verboseLogging}");

                using (var croppedBitmap = gameWindowBitmap.Clone(area, gameWindowBitmap.PixelFormat))
                {
                    // Call ProcessImage to ensure timing is captured
                    Log.Debug($"[Scanner.ProcessArea] Calling ProcessImage for category='{category}', field='{fieldName}'");

                    return ocrProcessor.ProcessImage(croppedBitmap, category, fieldName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process image area.");
                return new OCRResult { Text = string.Empty };
            }
        }

        private bool CompareImages(Bitmap image1, Bitmap image2, double threshold = 0.90) // Lowered threshold to be more forgiving
        {
            try
            {
                // Resize images if they don't match dimensions
                if (image1.Width != image2.Width || image1.Height != image2.Height)
                {
                    Log.Debug($"Resizing reference image from {image2.Width}x{image2.Height} to {image1.Width}x{image1.Height}");
                    using (var resized = new Bitmap(image2, image1.Width, image1.Height))
                    {
                        return CompareImagePixels(image1, resized, threshold);
                    }
                }

                return CompareImagePixels(image1, image2, threshold);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error comparing images");
                return false;
            }
        }

        private bool CompareImagePixels(Bitmap image1, Bitmap image2, double threshold)
        {
            int matchingPixels = 0;
            int totalPixels = image1.Width * image1.Height;

            for (int x = 0; x < image1.Width; x++)
            {
                for (int y = 0; y < image1.Height; y++)
                {
                    var pixel1 = image1.GetPixel(x, y);
                    var pixel2 = image2.GetPixel(x, y);

                    // Simple RGB comparison with tolerance
                    if (Math.Abs(pixel1.R - pixel2.R) < 10 &&
                        Math.Abs(pixel1.G - pixel2.G) < 10 &&
                        Math.Abs(pixel1.B - pixel2.B) < 10)
                    {
                        matchingPixels++;
                    }
                }
            }

            double similarity = (double)matchingPixels / totalPixels;
            Log.Debug($"Image comparison similarity: {similarity:P2} (threshold: {threshold:P2})");
            return similarity >= threshold;
        }
    }
}
