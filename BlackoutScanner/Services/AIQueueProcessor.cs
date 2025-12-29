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

        // Rate limiting tracking: providerId -> (requestTimes in current minute window)
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTime>> _providerRequestTimes;

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
            _providerRequestTimes = new ConcurrentDictionary<Guid, ConcurrentQueue<DateTime>>();

            Log.Information("AI Queue Processor initialized");
        }

        public void Enqueue(AIOCRQueueItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _queue.Enqueue(item);
            Log.Information($"[AIQueueProcessor] Item ENQUEUED: Category='{item.CategoryName}', Field='{item.FieldName}', OriginalText='{item.OriginalResult?.Text}', QueueSize={_queue.Count}");
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
            var overallStopwatch = Stopwatch.StartNew();
            var result = new AIOCRResult
            {
                QueueItemId = item.Id,
                CategoryName = item.CategoryName,
                FieldName = item.FieldName,
                ImageHash = item.ImageHash,
                RecordHash = item.RecordHash, // CRITICAL: Propagate record hash
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
                Log.Information($"[AIQueueProcessor] PROCESSING ITEM: Category='{item.CategoryName}', Field='{item.FieldName}', OriginalText='{result.OriginalOCRText}', OriginalConfidence={result.OriginalConfidence:F2}");

                // Check if AI enhancement is still enabled
                if (!_settingsManager.Settings.UseAIEnhancedOCR)
                {
                    Log.Warning($"[AIQueueProcessor] AI Enhancement disabled in settings, SKIPPING item: {item.CategoryName}/{item.FieldName}");
                    result.Success = false;
                    result.ErrorMessage = "AI Enhancement is disabled";
                    return;
                }

                // Get enabled AI providers sorted by priority (lower priority = tried first)
                var enabledProviders = _settingsManager.Settings.AIProviders
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.Priority)
                    .ToList();

                if (!enabledProviders.Any())
                {
                    Log.Warning($"[AIQueueProcessor] No enabled AI providers configured, SKIPPING item: {item.CategoryName}/{item.FieldName}");
                    result.Success = false;
                    result.ErrorMessage = "No AI providers are enabled";
                    return;
                }

                Log.Information($"[AIQueueProcessor] Found {enabledProviders.Count} enabled AI provider(s) to try");

                var confidenceThreshold = _settingsManager.Settings.OCRConfidenceThreshold;
                bool thresholdMet = false;

                // Try each provider in priority order
                foreach (var providerConfig in enabledProviders)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Check rate limit before attempting
                    if (!CanMakeRequest(providerConfig))
                    {
                        Log.Warning($"[AIQueueProcessor] Provider '{providerConfig.DisplayName}' has reached rate limit ({providerConfig.RequestsPerMinute} RPM), skipping to next provider");

                        var rateLimitAttempt = new AIAttempt
                        {
                            ProviderType = providerConfig.ProviderType,
                            Model = providerConfig.Model,
                            Priority = providerConfig.Priority,
                            Success = false,
                            ErrorMessage = $"Rate limit reached ({providerConfig.RequestsPerMinute} requests/minute)",
                            DurationMs = 0
                        };
                        result.Attempts.Add(rateLimitAttempt);
                        continue;
                    }

                    var attemptStopwatch = Stopwatch.StartNew();
                    var attempt = new AIAttempt
                    {
                        ProviderType = providerConfig.ProviderType,
                        Model = providerConfig.Model,
                        Priority = providerConfig.Priority
                    };

                    try
                    {
                        Log.Information($"[AIQueueProcessor] Trying provider: {providerConfig.DisplayName} (Priority {providerConfig.Priority})");

                        // Get the AI service for this provider
                        var aiService = GetAIService(providerConfig);
                        if (aiService == null)
                        {
                            attempt.Success = false;
                            attempt.ErrorMessage = $"Provider '{providerConfig.ProviderType}' not available";
                            attempt.DurationMs = attemptStopwatch.ElapsedMilliseconds;
                            result.Attempts.Add(attempt);
                            Log.Warning($"[AIQueueProcessor] {attempt.ErrorMessage}, skipping to next provider");
                            continue;
                        }

                        // Configure the service
                        ConfigureAIService(aiService, providerConfig);

                        // Check if configured
                        if (!aiService.IsConfigured())
                        {
                            attempt.Success = false;
                            attempt.ErrorMessage = "Service not configured (missing API key or model)";
                            attempt.DurationMs = attemptStopwatch.ElapsedMilliseconds;
                            result.Attempts.Add(attempt);
                            Log.Warning($"[AIQueueProcessor] Provider '{providerConfig.DisplayName}' is not configured, skipping");
                            continue;
                        }

                        // Record this request for rate limiting
                        RecordRequest(providerConfig);

                        // Perform AI OCR
                        var (aiText, aiConfidence) = await aiService.PerformOCRAsync(item.ImageData);

                        attemptStopwatch.Stop();
                        attempt.Success = true;
                        attempt.Text = aiText;
                        attempt.Confidence = aiConfidence;
                        attempt.DurationMs = attemptStopwatch.ElapsedMilliseconds;
                        result.Attempts.Add(attempt);

                        Log.Information($"[AIQueueProcessor] Provider '{providerConfig.DisplayName}' returned: Text='{aiText}', Confidence={aiConfidence:F2}%, Time={attempt.DurationMs}ms");

                        // Check if this result meets the confidence threshold
                        if (aiConfidence >= confidenceThreshold)
                        {
                            Log.Information($"[AIQueueProcessor] ✓ Confidence threshold MET ({aiConfidence:F2}% >= {confidenceThreshold:F2}%), stopping cascade");
                            thresholdMet = true;
                            break;
                        }
                        else
                        {
                            Log.Information($"[AIQueueProcessor] ✗ Confidence threshold NOT MET ({aiConfidence:F2}% < {confidenceThreshold:F2}%), trying next provider");
                        }
                    }
                    catch (Exception ex)
                    {
                        attemptStopwatch.Stop();
                        attempt.Success = false;
                        attempt.ErrorMessage = ex.Message;
                        attempt.DurationMs = attemptStopwatch.ElapsedMilliseconds;
                        result.Attempts.Add(attempt);
                        Log.Error(ex, $"[AIQueueProcessor] Provider '{providerConfig.DisplayName}' failed, trying next provider");
                    }
                }

                // Select the best result from all attempts
                if (result.Attempts.Any(a => a.Success))
                {
                    // Find the attempt with the highest confidence
                    var bestAttempt = result.Attempts
                        .Where(a => a.Success)
                        .OrderByDescending(a => a.Confidence)
                        .First();

                    result.SelectedAttemptIndex = result.Attempts.IndexOf(bestAttempt);
                    result.Success = true;
                    // Sanitize AI OCR text: replace newlines with spaces to ensure single-line output
                    result.Text = SanitizeOCRText(bestAttempt.Text);
                    result.Confidence = bestAttempt.Confidence;
                    result.AIProvider = bestAttempt.ProviderType;
                    result.Model = bestAttempt.Model;
                    result.AIDurationMs = result.Attempts.Sum(a => a.DurationMs);

                    // Determine application reason
                    if (thresholdMet)
                    {
                        result.ApplicationReason = $"Threshold met at priority {bestAttempt.Priority} ({bestAttempt.Confidence:F2}% >= {confidenceThreshold:F2}%)";
                    }
                    else if (result.Confidence > result.OriginalConfidence)
                    {
                        result.ApplicationReason = $"Best AI confidence ({result.Confidence:F2}%) higher than Tesseract ({result.OriginalConfidence:F2}%)";
                    }
                    else
                    {
                        result.ApplicationReason = $"Best available result (below threshold: {result.Confidence:F2}% < {confidenceThreshold:F2}%)";
                    }

                    _totalSucceeded++;
                    Log.Information($"[AIQueueProcessor] ✓ SELECTED: {bestAttempt.ProviderType}/{bestAttempt.Model} with {bestAttempt.Confidence:F2}% confidence. Reason: {result.ApplicationReason}");
                    Log.Information($"[AIQueueProcessor] Total attempts: {result.Attempts.Count}, Successful: {result.Attempts.Count(a => a.Success)}, Total time: {result.AIDurationMs}ms");
                }
                else
                {
                    // All attempts failed
                    result.Success = false;
                    result.ErrorMessage = $"All {result.Attempts.Count} provider(s) failed";
                    result.ApplicationReason = "All AI providers failed";
                    result.AIDurationMs = result.Attempts.Sum(a => a.DurationMs);
                    _totalFailed++;
                    Log.Error($"[AIQueueProcessor] ✗ ALL PROVIDERS FAILED for {item.CategoryName}/{item.FieldName}");
                }

                overallStopwatch.Stop();
                result.ProcessingTime = overallStopwatch.Elapsed;
                result.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ProcessingTime = overallStopwatch.Elapsed;
                result.ProcessedAt = DateTime.UtcNow;

                _totalFailed++;
                Log.Error(ex, $"AI OCR processing failed unexpectedly: Category={item.CategoryName}, Field={item.FieldName}");
            }
            finally
            {
                _totalProcessed++;
                _lastProcessedAt = DateTime.UtcNow;
                _processingTimes.Add(overallStopwatch.Elapsed);

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

        private IAIProvider? GetAIService(AIProviderConfiguration providerConfig)
        {
            try
            {
                return providerConfig.ProviderType switch
                {
                    // Unified NVIDIA provider (handles all NVIDIA models)
                    "Nvidia" => ServiceLocator.GetService<INvidiaOCRService>(),
                    // Legacy NVIDIA provider types (backward compatibility) - route to unified service
                    "NvidiaBuild" => ServiceLocator.GetService<INvidiaOCRService>(),
                    "NemotronParse" => ServiceLocator.GetService<INvidiaOCRService>(),
                    "Gemini" => new GeminiOCRService(),
                    "Groq" => new GroqOCRService(),
                    "Mistral" => new MistralOCRService(),
                    "Cohere" => new CohereOCRService(),
                    // Future providers:
                    // "OpenAI" => ServiceLocator.GetService<IOpenAIService>(),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to get AI service for provider '{providerConfig.ProviderType}'");
                return null;
            }
        }

        private void ConfigureAIService(IAIProvider service, AIProviderConfiguration providerConfig)
        {
            if (service is INvidiaOCRService nvidiaService)
            {
                // Unified NVIDIA service handles all NVIDIA models (PaddleOCR, Nemotron-Parse, etc.)
                nvidiaService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
                Log.Debug($"Configured NVIDIA service with model: {providerConfig.Model}");
            }
            else if (service is GeminiOCRService geminiService)
            {
                geminiService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
                Log.Debug($"Configured Gemini service with model: {providerConfig.Model}");
            }
            else if (service is GroqOCRService groqService)
            {
                groqService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
                Log.Debug($"Configured Groq service with model: {providerConfig.Model}");
            }
            else if (service is MistralOCRService mistralService)
            {
                mistralService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
                Log.Debug($"Configured Mistral service with model: {providerConfig.Model}");
            }
            else if (service is CohereOCRService cohereService)
            {
                cohereService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
                Log.Debug($"Configured Cohere service with model: {providerConfig.Model}");
            }
            // Future: Handle other providers
            // else if (service is IOpenAIService openAIService)
            // {
            //     openAIService.UpdateConfiguration(providerConfig.ApiKey, providerConfig.Model);
            // }
        }

        /// <summary>
        /// Checks if a request can be made to the specified provider based on rate limits.
        /// </summary>
        private bool CanMakeRequest(AIProviderConfiguration providerConfig)
        {
            if (providerConfig.RequestsPerMinute <= 0)
            {
                // No rate limit configured, allow request
                return true;
            }

            // Get or create request queue for this provider
            var requestQueue = _providerRequestTimes.GetOrAdd(providerConfig.Id, _ => new ConcurrentQueue<DateTime>());

            // Clean up old requests (older than 1 minute)
            var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
            while (requestQueue.TryPeek(out var oldestRequest) && oldestRequest < oneMinuteAgo)
            {
                requestQueue.TryDequeue(out _);
            }

            // Check if we can make a new request
            var currentRequestCount = requestQueue.Count;
            var canMakeRequest = currentRequestCount < providerConfig.RequestsPerMinute;

            if (canMakeRequest)
            {
                Log.Debug($"[RateLimit] Provider '{providerConfig.DisplayName}': {currentRequestCount}/{providerConfig.RequestsPerMinute} requests in current minute - OK");
            }
            else
            {
                Log.Warning($"[RateLimit] Provider '{providerConfig.DisplayName}': {currentRequestCount}/{providerConfig.RequestsPerMinute} requests in current minute - RATE LIMIT REACHED");
            }

            return canMakeRequest;
        }

        /// <summary>
        /// Records a request made to the specified provider for rate limiting tracking.
        /// </summary>
        private void RecordRequest(AIProviderConfiguration providerConfig)
        {
            if (providerConfig.RequestsPerMinute <= 0)
            {
                // No rate limiting, don't track
                return;
            }

            var requestQueue = _providerRequestTimes.GetOrAdd(providerConfig.Id, _ => new ConcurrentQueue<DateTime>());
            requestQueue.Enqueue(DateTime.UtcNow);

            Log.Debug($"[RateLimit] Recorded request for provider '{providerConfig.DisplayName}', total in window: {requestQueue.Count}");
        }

        /// <summary>
        /// Sanitizes AI OCR text by replacing newlines with spaces and trimming excess whitespace.
        /// This ensures OCR results are always single-line to prevent data entry issues.
        /// </summary>
        private static string SanitizeOCRText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Replace all types of newlines with a space
            var sanitized = text
                .Replace("\r\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");

            // Collapse multiple spaces into one and trim
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s+", " ").Trim();

            return sanitized;
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource?.Dispose();
            Log.Information("AI Queue Processor disposed");
        }
    }
}
