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
        private GameProfile? activeProfile;

        // Debug settings
        private bool saveDebugImages;
        private bool verboseLogging;
        private string debugImagesFolder;

        private readonly object bitmapLock = new object();

        public event Action<Dictionary<string, object>>? DataUpdated;
        public event Action<string, BitmapImage>? ImageUpdated;
        public event Action<DateTime>? ScanDateUpdated;
        public event Action<string>? CategoryScanning; // Add this new event

        public Scanner(IDataManager dataManager, IOCRProcessor ocrProcessor, IScreenCapture screenCapture)
        {
            this.dataManager = dataManager;
            this.ocrProcessor = ocrProcessor;
            this.screenCapture = screenCapture;

            // Default debug settings
            this.saveDebugImages = false;
            this.verboseLogging = false;
            this.debugImagesFolder = "DebugImages";

            // Log initial values
            Log.Information("[Scanner.Constructor] Scanner initialized with default settings: " +
                           $"saveDebugImages={this.saveDebugImages}, " +
                           $"verboseLogging={this.verboseLogging}, " +
                           $"debugImagesFolder={this.debugImagesFolder}");
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

                        var dataRecord = new DataRecord();
                        var updatedFields = new Dictionary<string, object>();

                        foreach (var field in category.Fields)
                        {
                            // Convert relative bounds to absolute for this window size
                            var fieldAbsoluteBounds = field.RelativeBounds.ToAbsolute(containerRect);

                            OCRResult fieldResult = ProcessArea(gameWindowBitmap, fieldAbsoluteBounds, false, category.Name, field.Name);
                            updatedFields[field.Name] = fieldResult.Text;

                            using (var fieldBitmap = gameWindowBitmap.Clone(fieldAbsoluteBounds, gameWindowBitmap.PixelFormat))
                            {
                                ImageUpdated?.Invoke(field.Name, ocrProcessor.ConvertBitmapToBitmapImage(fieldBitmap));
                            }
                        }

                        // IMPORTANT: Copy the updatedFields to dataRecord.Fields
                        dataRecord.Fields = new Dictionary<string, object>(updatedFields);
                        dataRecord.ScanDate = DateTime.UtcNow;
                        dataRecord.Category = category.Name; // Add category name
                        dataRecord.GameProfile = activeProfile.ProfileName; // Add profile name

                        DataUpdated?.Invoke(updatedFields);
                        ScanDateUpdated?.Invoke(dataRecord.ScanDate);

                        dataManager.AddOrUpdateRecord(dataRecord, activeProfile);
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

                lock (bitmapLock)
                {
                    using (var croppedBitmap = gameWindowBitmap.Clone(area, gameWindowBitmap.PixelFormat))
                    {
                        // Use the extended method with debug parameters
                        Log.Debug($"[Scanner.ProcessArea] Calling ProcessImageWithFallback with saveDebugImages={saveDebugImages}");

                        return ocrProcessor.ProcessImageWithFallback(
                            croppedBitmap,
                            0,
                            numericalOnly,
                            saveDebugImages,
                            debugImagesFolder,
                            verboseLogging,
                            category,
                            fieldName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process image area.");
                return new OCRResult { Text = string.Empty };
            }
        }

        private bool CompareImages(Bitmap image1, Bitmap image2, double threshold = 0.95)
        {
            try
            {
                // Resize images if they don't match dimensions
                if (image1.Width != image2.Width || image1.Height != image2.Height)
                {
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
            Log.Debug($"Image comparison similarity: {similarity:P2}");
            return similarity >= threshold;
        }
    }
}
