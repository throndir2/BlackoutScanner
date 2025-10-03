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
        /// When this item was added to the queue.
        /// </summary>
        public DateTime QueuedAt { get; set; }

        /// <summary>
        /// The AI provider to use (e.g., "NvidiaBuild").
        /// </summary>
        public string AIProvider { get; set; }

        /// <summary>
        /// Optional: The model to use for this specific request.
        /// If null, uses the configured default model.
        /// </summary>
        public string? ModelOverride { get; set; }

        /// <summary>
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
            QueuedAt = DateTime.UtcNow;
            AIProvider = "NvidiaBuild";
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
        /// Whether the AI processing was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The extracted text from the AI service.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Confidence score from the AI service (if available).
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
        /// Error message if processing failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When the processing was completed.
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// How long the processing took.
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// The AI provider used.
        /// </summary>
        public string AIProvider { get; set; }

        /// <summary>
        /// The model used for processing.
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

        public AIOCRResult()
        {
            Text = string.Empty;
            CategoryName = string.Empty;
            FieldName = string.Empty;
            ImageHash = string.Empty;
            ProcessedAt = DateTime.UtcNow;
            AIProvider = string.Empty;
            Model = string.Empty;
            OriginalOCRText = string.Empty;
        }
    }
}
