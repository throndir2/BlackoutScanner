using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using BlackoutScanner.Utilities;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlackoutScanner.Services
{
    /// <summary>
    /// Background queue processor for AI-enhanced OCR of low-confidence results.
    /// </summary>
    public class AIQueueProcessor : IAIQueueProcessor
    {
        private readonly ConcurrentQueue<AIOCRQueueItem> _queue;
        private readonly ISettingsManager _settingsManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task? _processingTask;
        private bool _isRunning;
        private readonly object _lockObject = new object();

        // Statistics
        private int _totalProcessed;
        private int _totalSucceeded;
        private int _totalFailed;
        private readonly List<TimeSpan> _processingTimes;
        private DateTime? _lastProcessedAt;

        public event EventHandler<AIOCRResult>? ItemProcessed;

        public int QueueCount => _queue.Count;

        public bool IsRunning => _isRunning;

        public AIQueueProcessor(ISettingsManager settingsManager)
        {
            _queue = new ConcurrentQueue<AIOCRQueueItem>();
            _settingsManager = settingsManager;
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTimes = new List<TimeSpan>();

            Log.Information("AI Queue Processor initialized");
        }

        public void Enqueue(AIOCRQueueItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _queue.Enqueue(item);
            Log.Information($"[AIQueueProcessor] Item ENQUEUED: Category='{item.CategoryName}', Field='{item.FieldName}', OriginalText='{item.OriginalResult?.Text}', Provider='{item.AIProvider}', QueueSize={_queue.Count}");
        }

        public void Start()
        {
            lock (_lockObject)
            {
                if (_isRunning)
                {
                    Log.Warning("AI Queue Processor is already running");
                    return;
                }

                _isRunning = true;
                _processingTask = Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token));
                Log.Information("AI Queue Processor started");
            }
        }

        public async Task StopAsync()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                {
                    return;
                }

                _isRunning = false;
            }

            Log.Information("Stopping AI Queue Processor...");

            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
                return;
            }

            if (_processingTask != null)
            {
                try
                {
                    // Wait for task with timeout to prevent hanging
                    var completedTask = await Task.WhenAny(_processingTask, Task.Delay(2000));
                    if (completedTask != _processingTask)
                    {
                        Log.Warning("AI Queue Processor did not stop within timeout, forcing shutdown");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping AI Queue Processor");
                }
            }

            Log.Information("AI Queue Processor stopped");
        }

        public void ClearQueue()
        {
            while (_queue.TryDequeue(out _)) { }
            Log.Information("AI Queue cleared");
        }

        public QueueStatistics GetStatistics()
        {
            return new QueueStatistics
            {
                TotalProcessed = _totalProcessed,
                TotalSucceeded = _totalSucceeded,
                TotalFailed = _totalFailed,
                CurrentQueueSize = _queue.Count,
                AverageProcessingTime = _processingTimes.Any()
                    ? TimeSpan.FromMilliseconds(_processingTimes.Average(t => t.TotalMilliseconds))
                    : TimeSpan.Zero,
                LastProcessedAt = _lastProcessedAt
            };
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            Log.Information("[AIQueueProcessor] Processing loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Try to get an item from the queue
                    if (_queue.TryDequeue(out var item))
                    {
                        Log.Information($"[AIQueueProcessor] Dequeued item for processing: Category='{item.CategoryName}', Field='{item.FieldName}', QueueSize={_queue.Count}");
                        await ProcessItemAsync(item, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when stopping
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in AI Queue processing loop");
                    await Task.Delay(1000, cancellationToken); // Wait before retrying
                }
            }

            Log.Information("AI Queue processing loop ended");
        }

        private async Task ProcessItemAsync(AIOCRQueueItem item, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new AIOCRResult
            {
                QueueItemId = item.Id,
                CategoryName = item.CategoryName,
                FieldName = item.FieldName,
                ImageHash = item.ImageHash,
                AIProvider = item.AIProvider,
                EnqueuedTime = item.QueuedAt
            };

            // Populate original OCR data from the queue item
            if (item.OriginalResult != null)
            {
                result.OriginalOCRText = item.OriginalResult.Text;
                result.TesseractDurationMs = item.OriginalResult.ProcessingTimeMs;

                Log.Debug($"[AIQueueProcessor] Populated from OriginalResult: Text='{result.OriginalOCRText}', ProcessingTimeMs={result.TesseractDurationMs}ms");

                // Calculate average confidence from word confidences
                if (item.OriginalResult.WordConfidences != null && item.OriginalResult.WordConfidences.Any())
                {
                    result.OriginalConfidence = item.OriginalResult.WordConfidences.Average(wc => wc.Confidence);
                }
            }
            else
            {
                Log.Warning($"[AIQueueProcessor] OriginalResult is NULL for item {item.CategoryName}/{item.FieldName}!");
            }

            try
            {
                Log.Information($"[AIQueueProcessor] PROCESSING ITEM: Category='{item.CategoryName}', Field='{item.FieldName}', Provider='{item.AIProvider}', OriginalText='{result.OriginalOCRText}', OriginalConfidence={result.OriginalConfidence:F2}");

                // Check if AI enhancement is still enabled
                if (!_settingsManager.Settings.UseAIEnhancedOCR)
                {
                    Log.Warning($"[AIQueueProcessor] AI Enhancement disabled in settings, SKIPPING item: {item.CategoryName}/{item.FieldName}");
                    result.Success = false;
                    result.ErrorMessage = "AI Enhancement is disabled";
                    return;
                }

                // Get the appropriate AI service
                var aiService = GetAIService(item.AIProvider);
                if (aiService == null)
                {
                    Log.Error($"AI provider '{item.AIProvider}' not found or not configured");
                    result.Success = false;
                    result.ErrorMessage = $"AI provider '{item.AIProvider}' not available";
                    return;
                }

                // Update configuration if needed
                UpdateServiceConfiguration(aiService, item);

                // Check if configured
                if (!aiService.IsConfigured())
                {
                    Log.Warning($"AI service '{item.AIProvider}' is not configured");
                    result.Success = false;
                    result.ErrorMessage = "AI service is not configured (missing API key or model)";
                    return;
                }

                // Perform AI OCR
                var (aiText, aiConfidence) = await aiService.PerformOCRAsync(item.ImageData);

                result.Success = true;
                result.Text = aiText;
                result.Confidence = aiConfidence;
                result.Model = aiService is INvidiaAIService nvidiaService ? nvidiaService.Model : "unknown";

                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                result.AIDurationMs = stopwatch.ElapsedMilliseconds;
                result.ProcessedAt = DateTime.UtcNow;

                _totalSucceeded++;
                Log.Information($"AI OCR succeeded: Text='{aiText}', Confidence={aiConfidence:F2}%, Time={stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ProcessingTime = stopwatch.Elapsed;
                result.AIDurationMs = stopwatch.ElapsedMilliseconds;
                result.ProcessedAt = DateTime.UtcNow;

                _totalFailed++;
                Log.Error(ex, $"AI OCR failed: Category={item.CategoryName}, Field={item.FieldName}");
            }
            finally
            {
                _totalProcessed++;
                _lastProcessedAt = DateTime.UtcNow;
                _processingTimes.Add(stopwatch.Elapsed);

                // Keep only last 100 processing times for average calculation
                if (_processingTimes.Count > 100)
                {
                    _processingTimes.RemoveAt(0);
                }

                // Invoke completion callback if provided
                item.OnCompleted?.Invoke(result);

                // Raise event
                ItemProcessed?.Invoke(this, result);
            }
        }

        private IAIProvider? GetAIService(string providerName)
        {
            try
            {
                return providerName switch
                {
                    "NvidiaBuild" => ServiceLocator.GetService<INvidiaAIService>(),
                    // Future providers:
                    // "OpenAI" => ServiceLocator.GetService<IOpenAIService>(),
                    // "Gemini" => ServiceLocator.GetService<IGeminiService>(),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to get AI service for provider '{providerName}'");
                return null;
            }
        }

        private void UpdateServiceConfiguration(IAIProvider service, AIOCRQueueItem item)
        {
            if (service is INvidiaAIService nvidiaService)
            {
                var apiKey = _settingsManager.Settings.NvidiaApiKey;
                var model = item.ModelOverride ?? _settingsManager.Settings.NvidiaModel;
                nvidiaService.UpdateConfiguration(apiKey, model);
            }
            // Future: Handle other providers
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource?.Dispose();
            Log.Information("AI Queue Processor disposed");
        }
    }
}
