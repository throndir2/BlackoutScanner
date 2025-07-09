using BlackoutScanner.Models;
using Serilog;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace BlackoutScanner
{
    public class Scanner
    {
        private bool continueScanning = false;
        private readonly DataManager dataManager;
        private readonly OCRProcessor ocrProcessor;
        private readonly ScreenCapture screenCapture;
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

        public Scanner(DataManager dataManager, OCRProcessor ocrProcessor)
        {
            this.dataManager = dataManager;
            this.ocrProcessor = ocrProcessor;
            this.screenCapture = new ScreenCapture();

            // Default debug settings
            this.saveDebugImages = false;
            this.verboseLogging = false;
            this.debugImagesFolder = "DebugImages";
        }

        public void UpdateDebugSettings(bool saveDebugImages, bool verboseLogging, string debugImagesFolder)
        {
            this.saveDebugImages = saveDebugImages;
            this.verboseLogging = verboseLogging;
            this.debugImagesFolder = debugImagesFolder;

            Log.Information($"Scanner debug settings updated: saveDebugImages={saveDebugImages}, verboseLogging={verboseLogging}, folder={debugImagesFolder}");
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

                    OCRResult categoryResult = ProcessArea(gameWindowBitmap, categoryAbsoluteBounds, false, "CategoryHeader", category.Name);
                    if (categoryResult.Text.Trim().Equals(category.Name, StringComparison.OrdinalIgnoreCase))
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
                lock (bitmapLock)
                {
                    using (var croppedBitmap = gameWindowBitmap.Clone(area, gameWindowBitmap.PixelFormat))
                    {
                        // Use the extended method with debug parameters
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
    }
}
