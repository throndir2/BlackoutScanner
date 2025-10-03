using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BlackoutScanner.Services
{
    /// <summary>
    /// Service for interacting with NVIDIA Build AI OCR APIs.
    /// Supports PaddleOCR and NeMo Retriever OCR models.
    /// </summary>
    public class NvidiaOCRService : INvidiaOCRService
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URL for NVIDIA Build API
        private const string BaseUrl = "https://ai.api.nvidia.com/v1/cv";

        // Maximum base64 image size (180KB as per NVIDIA docs)
        private const int MaxImageSizeBase64 = 180_000;

        public string ProviderName => "NvidiaBuild";

        public string ApiKey
        {
            get => _apiKey;
            set => _apiKey = value;
        }

        public string Model
        {
            get => _model;
            set => _model = value;
        }

        public NvidiaOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _apiKey = string.Empty;
            _model = "baidu/paddleocr";
        }

        public void UpdateConfiguration(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
            Log.Information($"NVIDIA OCR Service configuration updated: Model={model}");
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                Log.Warning("NVIDIA OCR Service is not properly configured");
                return false;
            }

            try
            {
                // Create a minimal test request with a 1x1 transparent PNG
                var testImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
                var invokeUrl = GetInvokeUrl();

                var request = new NvidiaOCRRequest
                {
                    Input = new List<NvidiaInputItem>
                    {
                        new NvidiaInputItem
                        {
                            Type = "image_url",
                            Url = $"data:image/png;base64,{testImageBase64}"
                        }
                    }
                };

                var response = await SendRequestAsync(invokeUrl, request);

                Log.Information("NVIDIA OCR Service connection test successful");
                return response != null && !response.HasError;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NVIDIA OCR Service connection test failed");
                return false;
            }
        }

        public async Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("NVIDIA OCR Service is not properly configured. Please set API key and model.");
            }

            if (imageData == null || imageData.Length == 0)
            {
                throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));
            }

            try
            {
                // Convert image to base64
                var imageBase64 = Convert.ToBase64String(imageData);

                // Check size limit
                if (imageBase64.Length > MaxImageSizeBase64)
                {
                    throw new InvalidOperationException(
                        $"Image size ({imageBase64.Length} chars) exceeds maximum allowed size ({MaxImageSizeBase64} chars). " +
                        "Consider using the NVIDIA assets API for larger images.");
                }

                var invokeUrl = GetInvokeUrl();

                var request = new NvidiaOCRRequest
                {
                    Input = new List<NvidiaInputItem>
                    {
                        new NvidiaInputItem
                        {
                            Type = "image_url",
                            Url = $"data:image/png;base64,{imageBase64}"
                        }
                    }
                };

                Log.Debug($"Sending OCR request to NVIDIA API: {invokeUrl}");
                var response = await SendRequestAsync(invokeUrl, request);

                if (response == null)
                {
                    throw new Exception("Received null response from NVIDIA API");
                }

                if (response.HasError)
                {
                    throw new Exception($"NVIDIA API returned error: {response.Error?.Message ?? "Unknown error"}");
                }

                // Extract text and confidence from response
                var (extractedText, confidence) = ExtractTextFromResponse(response);
                Log.Information($"NVIDIA OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

                return (extractedText, confidence);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using NVIDIA OCR Service");
                throw;
            }
        }

        private string GetInvokeUrl()
        {
            return $"{BaseUrl}/{_model}";
        }

        private async Task<NvidiaOCRResponse?> SendRequestAsync(string url, NvidiaOCRRequest request)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);

            // Set headers
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
            httpRequest.Headers.Add("Accept", "application/json");

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(request);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"NVIDIA API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"NVIDIA API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            // Deserialize response
            var response = JsonConvert.DeserializeObject<NvidiaOCRResponse>(responseContent);
            return response;
        }

        private (string text, float confidence) ExtractTextFromResponse(NvidiaOCRResponse response)
        {
            if (response.Data == null || response.Data.Count == 0)
            {
                Log.Warning("NVIDIA API response contains no data");
                return (string.Empty, 0f);
            }

            var textBuilder = new StringBuilder();
            var confidences = new List<float>();

            foreach (var dataItem in response.Data)
            {
                // Check if this is PaddleOCR format (has text_detections)
                if (dataItem.TextDetections != null && dataItem.TextDetections.Count > 0)
                {
                    // Extract text from all detections
                    foreach (var detection in dataItem.TextDetections)
                    {
                        if (detection.TextPrediction != null && !string.IsNullOrWhiteSpace(detection.TextPrediction.Text))
                        {
                            textBuilder.AppendLine(detection.TextPrediction.Text);
                            confidences.Add(detection.TextPrediction.Confidence);
                            Log.Debug($"Extracted text: '{detection.TextPrediction.Text}' (confidence: {detection.TextPrediction.Confidence:P2})");
                        }
                    }
                }
                // Otherwise use simple content format
                else if (!string.IsNullOrWhiteSpace(dataItem.Content))
                {
                    textBuilder.AppendLine(dataItem.Content);
                    // Simple format doesn't include confidence, assume high confidence
                    confidences.Add(0.95f);
                }
            }

            var finalText = textBuilder.ToString().Trim();
            var averageConfidence = confidences.Any() ? confidences.Average() : 0f;

            // Convert to percentage (0-100)
            averageConfidence *= 100f;

            Log.Debug($"Final extracted text: '{finalText}', Average confidence: {averageConfidence:F2}%");
            return (finalText, averageConfidence);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
