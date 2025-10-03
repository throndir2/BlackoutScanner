using BlackoutScanner.Models;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BlackoutScanner.ViewModels
{
    /// <summary>
    /// ViewModel for the AI Queue Monitor view.
    /// Tracks pending and processed AI OCR items with performance metrics.
    /// </summary>
    public class AIQueueMonitorViewModel : INotifyPropertyChanged
    {
        private int _pendingCount;
        private int _processedCount;
        private int _successCount;
        private int _failedCount;
        private double _averageTesseractMs;
        private double _averageAIMs;
        private double _successRate;
        private string _statusMessage;

        public ObservableCollection<AIOCRQueueItem> PendingItems { get; }
        public ObservableCollection<AIOCRResult> ProcessedItems { get; }

        public int PendingCount
        {
            get => _pendingCount;
            set { _pendingCount = value; OnPropertyChanged(); }
        }

        public int ProcessedCount
        {
            get => _processedCount;
            set { _processedCount = value; OnPropertyChanged(); }
        }

        public int SuccessCount
        {
            get => _successCount;
            set { _successCount = value; OnPropertyChanged(); }
        }

        public int FailedCount
        {
            get => _failedCount;
            set { _failedCount = value; OnPropertyChanged(); }
        }

        public double AverageTesseractMs
        {
            get => _averageTesseractMs;
            set { _averageTesseractMs = value; OnPropertyChanged(); }
        }

        public double AverageAIMs
        {
            get => _averageAIMs;
            set { _averageAIMs = value; OnPropertyChanged(); }
        }

        public double SuccessRate
        {
            get => _successRate;
            set { _successRate = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public AIQueueMonitorViewModel()
        {
            PendingItems = new ObservableCollection<AIOCRQueueItem>();
            ProcessedItems = new ObservableCollection<AIOCRResult>();
            _statusMessage = "No items processed yet";
        }

        /// <summary>
        /// Adds an item to the pending queue.
        /// </summary>
        public void AddPendingItem(AIOCRQueueItem item)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PendingItems.Add(item);
                PendingCount = PendingItems.Count;
                UpdateStatusMessage();
            });
        }

        /// <summary>
        /// Removes an item from pending and adds it to processed.
        /// </summary>
        public void MoveToProcessed(AIOCRResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Log.Debug($"[AIQueueMonitorViewModel] MoveToProcessed called: Category='{result.CategoryName}', Field='{result.FieldName}', TesseractMs={result.TesseractDurationMs}, AIMs={result.AIDurationMs}");

                // Remove from pending by matching QueueItemId
                var pendingItem = PendingItems.FirstOrDefault(p => p.Id == result.QueueItemId);
                if (pendingItem != null)
                {
                    PendingItems.Remove(pendingItem);
                    Log.Debug($"[AIQueueMonitorViewModel] Removed from pending queue");
                }
                else
                {
                    Log.Debug($"[AIQueueMonitorViewModel] No matching pending item found for QueueItemId={result.QueueItemId}");
                }

                // Add to processed (insert at beginning for most recent first)
                ProcessedItems.Insert(0, result);
                Log.Debug($"[AIQueueMonitorViewModel] Added to processed collection. Collection size: {ProcessedItems.Count}");

                // Update counts
                PendingCount = PendingItems.Count;
                ProcessedCount = ProcessedItems.Count;

                if (result.Success)
                {
                    SuccessCount++;
                }
                else
                {
                    FailedCount++;
                }

                // Update performance metrics
                RecalculateMetrics();
                Log.Information($"[AIQueueMonitorViewModel] Metrics updated: Pending={PendingCount}, Processed={ProcessedCount}, Success={SuccessCount}, Failed={FailedCount}, AvgTesseract={AverageTesseractMs:F2}ms, AvgAI={AverageAIMs:F2}ms");
                UpdateStatusMessage();
            });
        }

        /// <summary>
        /// Recalculates performance statistics based on processed items.
        /// </summary>
        private void RecalculateMetrics()
        {
            if (ProcessedItems.Count == 0)
            {
                AverageTesseractMs = 0;
                AverageAIMs = 0;
                SuccessRate = 0;
                Log.Debug($"[AIQueueMonitorViewModel] RecalculateMetrics: No processed items, all metrics set to 0");
                return;
            }

            // Calculate average Tesseract time
            var tesseractTimes = ProcessedItems
                .Where(p => p.TesseractDurationMs > 0)
                .Select(p => p.TesseractDurationMs);

            AverageTesseractMs = tesseractTimes.Any() ? tesseractTimes.Average() : 0;

            // Calculate average AI time
            var aiTimes = ProcessedItems
                .Where(p => p.AIDurationMs > 0)
                .Select(p => p.AIDurationMs);

            AverageAIMs = aiTimes.Any() ? aiTimes.Average() : 0;

            // Calculate success rate
            SuccessRate = (double)SuccessCount / ProcessedCount * 100;

            Log.Debug($"[AIQueueMonitorViewModel] RecalculateMetrics: TesseractSamples={tesseractTimes.Count()}, Avg={AverageTesseractMs:F2}ms, AISamples={aiTimes.Count()}, Avg={AverageAIMs:F2}ms, SuccessRate={SuccessRate:F1}%");
        }

        private void UpdateStatusMessage()
        {
            if (ProcessedCount == 0 && PendingCount == 0)
            {
                StatusMessage = "No items in queue";
            }
            else if (PendingCount > 0)
            {
                StatusMessage = $"Processing {PendingCount} item{(PendingCount > 1 ? "s" : "")}...";
            }
            else
            {
                StatusMessage = $"All items processed. Success rate: {SuccessRate:F1}%";
            }
        }

        /// <summary>
        /// Clears all processed items.
        /// </summary>
        public void ClearProcessed()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProcessedItems.Clear();
                ProcessedCount = 0;
                SuccessCount = 0;
                FailedCount = 0;
                RecalculateMetrics();
                UpdateStatusMessage();
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
