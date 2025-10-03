using BlackoutScanner.Models;
using System;
using System.Threading.Tasks;

namespace BlackoutScanner.Interfaces
{
    /// <summary>
    /// Interface for the AI OCR queue processor that handles background processing
    /// of low-confidence OCR results using AI services.
    /// </summary>
    public interface IAIQueueProcessor : IDisposable
    {
        /// <summary>
        /// Event raised when an item has been processed.
        /// </summary>
        event EventHandler<AIOCRResult>? ItemProcessed;

        /// <summary>
        /// Gets the number of items currently in the queue.
        /// </summary>
        int QueueCount { get; }

        /// <summary>
        /// Gets whether the queue processor is currently running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Adds an item to the processing queue.
        /// </summary>
        /// <param name="item">The queue item to process.</param>
        void Enqueue(AIOCRQueueItem item);

        /// <summary>
        /// Starts the background processing of queued items.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the background processing.
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Clears all items from the queue.
        /// </summary>
        void ClearQueue();

        /// <summary>
        /// Gets statistics about queue processing.
        /// </summary>
        QueueStatistics GetStatistics();
    }

    /// <summary>
    /// Statistics about the AI queue processor.
    /// </summary>
    public class QueueStatistics
    {
        public int TotalProcessed { get; set; }
        public int TotalSucceeded { get; set; }
        public int TotalFailed { get; set; }
        public int CurrentQueueSize { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public DateTime? LastProcessedAt { get; set; }
    }
}
