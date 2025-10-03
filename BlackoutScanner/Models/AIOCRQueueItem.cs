using System;

namespace BlackoutScanner.Models
{
    /// <summary>
    /// Represents an item in the AI OCR processing queue.
    /// Contains all information needed to re-process a low-confidence OCR result.
    /// </summary>
    public class AIOCRQueueItem
    {
        /// <summary>
        /// Unique identifier for this queue item.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The image data to process (PNG format as byte array).
        /// </summary>
        public byte[] ImageData { get; set; }

        /// <summary>
        /// The original OCR result from Tesseract (low confidence).
        /// </summary>
        public OCRResult OriginalResult { get; set; }

        /// <summary>
        /// The category this field belongs to (e.g., "Player Info").
        /// </summary>
        public string CategoryName { get; set; }

        /// <summary>
        /// The field name being processed (e.g., "Player Name", "Alliance").
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// The hash of the image data for cache lookup.
        /// </summary>
        public string ImageHash { get; set; }

        /// <summary>
        /// The hash of the DataRecord this field belongs to.
        /// This is critical for identifying the correct record when AI results return asynchronously.
        /// </summary>
        public string RecordHash { get; set; }

        /// <summary>
        /// When this item was added to the queue.
        /// </summary>
        public DateTime QueuedAt { get; set; }        /// <summary>
                                                      /// Optional: Callback to invoke when processing completes.
                                                      /// </summary>
        public Action<AIOCRResult>? OnCompleted { get; set; }

        public AIOCRQueueItem()
        {
            Id = Guid.NewGuid();
            ImageData = Array.Empty<byte>();
            OriginalResult = new OCRResult();
            CategoryName = string.Empty;
            FieldName = string.Empty;
            ImageHash = string.Empty;
            RecordHash = string.Empty;
            QueuedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Represents a single attempt at AI OCR processing.
    /// </summary>
    public class AIAttempt
    {
        /// <summary>
        /// The provider type used for this attempt (e.g., "NvidiaBuild", "Gemini").
        /// </summary>
        public string ProviderType { get; set; }

        /// <summary>
        /// The specific model used (e.g., "baidu/paddleocr", "gemini-1.5-flash").
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// The extracted text from this attempt.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Confidence score for this attempt (0-100).
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// How long this attempt took in milliseconds.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Whether this attempt succeeded or failed.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if the attempt failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Priority of this provider at the time of the attempt.
        /// </summary>
        public int Priority { get; set; }

        public AIAttempt()
        {
            ProviderType = string.Empty;
            Model = string.Empty;
            Text = string.Empty;
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"{ProviderType}/{Model}: \"{Text}\" ({Confidence:F2}%) in {DurationMs}ms";
            }
            else
            {
                return $"{ProviderType}/{Model}: FAILED - {ErrorMessage}";
            }
        }
    }

    /// <summary>
    /// Represents the result of AI OCR processing.
    /// </summary>
    public class AIOCRResult
    {
        /// <summary>
        /// The ID of the queue item that was processed.
        /// </summary>
        public Guid QueueItemId { get; set; }

        /// <summary>
        /// List of all AI provider attempts made for this item.
        /// Ordered by the sequence they were tried.
        /// </summary>
        public List<AIAttempt> Attempts { get; set; }

        /// <summary>
        /// Index of the attempt that was selected as the final result.
        /// -1 if no attempts succeeded or if using original Tesseract result.
        /// </summary>
        public int SelectedAttemptIndex { get; set; }

        /// <summary>
        /// Whether any AI processing was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The final extracted text (from the best AI attempt or original Tesseract).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The final confidence score (from the selected attempt).
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// The category name from the original request.
        /// </summary>
        public string CategoryName { get; set; }

        /// <summary>
        /// The field name from the original request.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// The hash of the image data for cache storage.
        /// </summary>
        public string ImageHash { get; set; }

        /// <summary>
        /// The hash of the DataRecord this field belongs to.
        /// Used to identify the correct record when updating with AI results.
        /// </summary>
        public string RecordHash { get; set; }

        /// <summary>
        /// Error message if all processing failed.
        /// </summary>
        public string? ErrorMessage { get; set; }        /// <summary>
                                                         /// When the processing was completed.
                                                         /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Total processing time for all attempts.
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// The AI provider that produced the final result (if any).
        /// Empty string if using original Tesseract result.
        /// </summary>
        public string AIProvider { get; set; }

        /// <summary>
        /// The model that produced the final result (if any).
        /// </summary>
        public string Model { get; set; }

        // ===== Performance Tracking Properties =====

        /// <summary>
        /// Original OCR text from Tesseract.
        /// </summary>
        public string OriginalOCRText { get; set; }

        /// <summary>
        /// Original confidence from Tesseract OCR.
        /// </summary>
        public float OriginalConfidence { get; set; }

        /// <summary>
        /// Duration of Tesseract OCR in milliseconds.
        /// </summary>
        public long TesseractDurationMs { get; set; }

        /// <summary>
        /// When the item was enqueued.
        /// </summary>
        public DateTime EnqueuedTime { get; set; }

        /// <summary>
        /// Duration of AI OCR in milliseconds.
        /// </summary>
        public long AIDurationMs { get; set; }

        /// <summary>
        /// Whether the AI result was applied (true) or rejected in favor of the original Tesseract result (false).
        /// </summary>
        public bool WasApplied { get; set; }

        /// <summary>
        /// Reason why the AI result was applied or rejected (e.g., "AI confidence higher", "Tesseract confidence higher").
        /// </summary>
        public string ApplicationReason { get; set; }

        public AIOCRResult()
        {
            Attempts = new List<AIAttempt>();
            SelectedAttemptIndex = -1;
            Text = string.Empty;
            CategoryName = string.Empty;
            FieldName = string.Empty;
            ImageHash = string.Empty;
            RecordHash = string.Empty;
            ProcessedAt = DateTime.UtcNow;
            AIProvider = string.Empty;
            Model = string.Empty;
            OriginalOCRText = string.Empty;
            ApplicationReason = string.Empty;
        }
    }
}
