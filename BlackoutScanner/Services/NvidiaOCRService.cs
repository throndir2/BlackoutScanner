using BlackoutScanner.Interfaces;
using BlackoutScanner.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlackoutScanner.Services
{
    /// <summary>
    /// Unified service for interacting with NVIDIA AI OCR APIs.
    /// Supports PaddleOCR (Build API) and Nemotron-Parse (Integrate API) models.
    /// Routes to the correct API based on model name.
    /// </summary>
    public class NvidiaOCRService : INvidiaOCRService
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URLs for different NVIDIA APIs
        private const string BuildApiBaseUrl = "https://ai.api.nvidia.com/v1/cv";
        private const string IntegrateApiBaseUrl = "https://integrate.api.nvidia.com/v1/chat/completions";

        // Maximum base64 image size (180KB as per NVIDIA docs for Build API)
        private const int MaxImageSizeBase64 = 180_000;

        public string ProviderName => "Nvidia";

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

        /// <summary>
        /// Determines if the current model uses the Nemotron-Parse chat completions API.
        /// </summary>
        private bool IsNemotronParseModel => _model?.Contains("nemotron-parse", StringComparison.OrdinalIgnoreCase) == true;

        public NvidiaOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Increased for Nemotron-Parse
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

                if (IsNemotronParseModel)
                {
                    // Test Nemotron-Parse API (chat completions)
                    var response = await SendChatCompletionRequestAsync(testImageBase64, "image/png", "markdown_no_bbox");
                    Log.Information("NVIDIA Nemotron-Parse connection test successful");
                    return response != null;
                }
                else
                {
                    // Test Build API (PaddleOCR format)
                    var invokeUrl = GetBuildApiUrl();
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

                    var response = await SendBuildApiRequestAsync(invokeUrl, request);
                    Log.Information("NVIDIA Build API connection test successful");
                    return response != null && !response.HasError;
                }
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

                // Route to appropriate API based on model
                if (IsNemotronParseModel)
                {
                    return await PerformNemotronParseOCRAsync(imageBase64);
                }
                else
                {
                    return await PerformBuildApiOCRAsync(imageBase64);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using NVIDIA OCR Service");
                throw;
            }
        }

        /// <summary>
        /// Performs OCR using NVIDIA Build API (PaddleOCR, NeMo Retriever).
        /// </summary>
        private async Task<(string text, float confidence)> PerformBuildApiOCRAsync(string imageBase64)
        {
            // Check size limit for Build API
            if (imageBase64.Length > MaxImageSizeBase64)
            {
                throw new InvalidOperationException(
                    $"Image size ({imageBase64.Length} chars) exceeds maximum allowed size ({MaxImageSizeBase64} chars). " +
                    "Consider using the NVIDIA assets API for larger images.");
            }

            var invokeUrl = GetBuildApiUrl();

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

            Log.Debug($"Sending OCR request to NVIDIA Build API: {invokeUrl}");
            var response = await SendBuildApiRequestAsync(invokeUrl, request);

            if (response == null)
            {
                throw new Exception("Received null response from NVIDIA Build API");
            }

            if (response.HasError)
            {
                throw new Exception($"NVIDIA Build API returned error: {response.Error?.Message ?? "Unknown error"}");
            }

            // Extract text and confidence from response
            var (extractedText, confidence) = ExtractTextFromBuildApiResponse(response);
            Log.Information($"NVIDIA Build API OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

            return (extractedText, confidence);
        }

        /// <summary>
        /// Performs OCR using NVIDIA Nemotron-Parse via chat completions API.
        /// </summary>
        private async Task<(string text, float confidence)> PerformNemotronParseOCRAsync(string imageBase64)
        {
            Log.Debug($"Sending OCR request to NVIDIA Nemotron-Parse API: {IntegrateApiBaseUrl}");

            // Use markdown_no_bbox for cleaner text extraction
            var response = await SendChatCompletionRequestAsync(imageBase64, "image/png", "markdown_no_bbox");

            if (response == null)
            {
                throw new Exception("Received null response from NVIDIA Nemotron-Parse API");
            }

            // Extract text from response
            var (extractedText, confidence) = ExtractTextFromChatCompletionResponse(response);
            Log.Information($"NVIDIA Nemotron-Parse OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

            return (extractedText, confidence);
        }

        private string GetBuildApiUrl()
        {
            return $"{BuildApiBaseUrl}/{_model}";
        }

        private async Task<NvidiaOCRResponse?> SendBuildApiRequestAsync(string url, NvidiaOCRRequest request)
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
                Log.Error($"NVIDIA Build API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"NVIDIA Build API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            // Deserialize response
            var response = JsonConvert.DeserializeObject<NvidiaOCRResponse>(responseContent);
            return response;
        }

        private async Task<JObject?> SendChatCompletionRequestAsync(string imageBase64, string mimeType, string toolName)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, IntegrateApiBaseUrl);

            // Set headers
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");
            httpRequest.Headers.Add("Accept", "application/json");

            // Build the content with embedded image (HTML img tag format as per NVIDIA docs)
            var imageContent = $"<img src=\"data:{mimeType};base64,{imageBase64}\" />";

            // Build the request payload (OpenAI-compatible format)
            var requestPayload = new
            {
                model = _model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = imageContent
                    }
                },
                tools = new[]
                {
                    new
                    {
                        type = "function",
                        function = new
                        {
                            name = toolName
                        }
                    }
                },
                tool_choice = new
                {
                    type = "function",
                    function = new
                    {
                        name = toolName
                    }
                },
                max_tokens = 8192
            };

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(requestPayload);
            
            // NVIDIA API requires exactly "application/json" without charset
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8);
            httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            Log.Debug($"Nemotron-Parse request payload: tool={toolName}, model={_model}");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"Nemotron-Parse API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"Nemotron-Parse API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            // Log the raw response for debugging
            Log.Debug($"Nemotron-Parse raw response: {responseContent}");

            // Parse response as JObject for flexible handling
            var response = JObject.Parse(responseContent);
            return response;
        }

        private (string text, float confidence) ExtractTextFromBuildApiResponse(NvidiaOCRResponse response)
        {
            if (response.Data == null || response.Data.Count == 0)
            {
                Log.Warning("NVIDIA Build API response contains no data");
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

        private (string text, float confidence) ExtractTextFromChatCompletionResponse(JObject response)
        {
            try
            {
                // Navigate the OpenAI-style response structure
                // choices[0].message.tool_calls[0].function.arguments
                var choices = response["choices"] as JArray;
                if (choices == null || choices.Count == 0)
                {
                    Log.Warning("Nemotron-Parse response contains no choices");
                    return (string.Empty, 0f);
                }

                var message = choices[0]?["message"];
                if (message == null)
                {
                    Log.Warning("Nemotron-Parse response contains no message");
                    return (string.Empty, 0f);
                }

                // Try to get tool_calls first (structured output)
                var toolCalls = message["tool_calls"] as JArray;
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    var functionArgs = toolCalls[0]?["function"]?["arguments"]?.ToString();
                    if (!string.IsNullOrEmpty(functionArgs))
                    {
                        // The arguments contain the extracted text (may be JSON or markdown)
                        var extractedText = CleanExtractedText(functionArgs);

                        // If we got empty text, return low confidence so cascade continues
                        if (string.IsNullOrWhiteSpace(extractedText))
                        {
                            Log.Warning("Nemotron-Parse returned empty text from tool_calls");
                            return (string.Empty, 0f);
                        }

                        // Nemotron-Parse doesn't provide explicit confidence, assume high confidence
                        var confidence = 90f;

                        Log.Debug($"Extracted text from tool_calls: '{extractedText.Substring(0, Math.Min(100, extractedText.Length))}...'");
                        return (extractedText, confidence);
                    }
                }

                // Fallback: try content field directly
                var content = message["content"]?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    var extractedText = CleanExtractedText(content);
                    
                    // If empty, return low confidence
                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        Log.Warning("Nemotron-Parse returned empty text from content");
                        return (string.Empty, 0f);
                    }
                    
                    return (extractedText, 85f);
                }

                Log.Warning("Nemotron-Parse response contains no extractable text");
                return (string.Empty, 0f);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error extracting text from Nemotron-Parse response");
                return (string.Empty, 0f);
            }
        }

        private string CleanExtractedText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            Log.Debug($"CleanExtractedText input: '{rawText}'");

            // Try to parse as JSON first (tool arguments might be JSON)
            try
            {
                // First try to parse as JArray (Nemotron-Parse often returns arrays like [{"text": "..."}])
                if (rawText.TrimStart().StartsWith("["))
                {
                    var jsonArray = JArray.Parse(rawText);
                    
                    // Log the array structure for debugging
                    Log.Debug($"Parsed JSON array with {jsonArray.Count} items");
                    
                    // If empty array, this means no text was detected
                    if (jsonArray.Count == 0)
                    {
                        Log.Warning("Nemotron-Parse returned empty JSON array - no text detected in image");
                        return string.Empty;
                    }
                    
                    var textParts = new List<string>();
                    
                    foreach (var item in jsonArray)
                    {
                        Log.Debug($"Array item: {item}");
                        
                        // Try to get text from each item - check various field names
                        var textField = item["text"] ?? item["content"] ?? item["markdown"] ?? item["result"] ?? item["value"];
                        if (textField != null && !string.IsNullOrWhiteSpace(textField.ToString()))
                        {
                            textParts.Add(textField.ToString());
                        }
                        else if (item.Type == JTokenType.String)
                        {
                            // Item might be a direct string
                            var itemStr = item.ToString();
                            if (!string.IsNullOrWhiteSpace(itemStr))
                            {
                                textParts.Add(itemStr);
                            }
                        }
                        else
                        {
                            // Log the item structure to understand the format
                            Log.Debug($"Unknown item structure, type={item.Type}, keys={string.Join(",", (item as JObject)?.Properties().Select(p => p.Name) ?? Array.Empty<string>())}");
                        }
                    }
                    
                    if (textParts.Count > 0)
                    {
                        rawText = string.Join("\n", textParts);
                        Log.Debug($"Extracted text from JSON array: '{rawText}'");
                    }
                    else
                    {
                        // Array had items but no text content
                        Log.Warning("Nemotron-Parse returned JSON array with no text content");
                        return string.Empty;
                    }
                }
                else
                {
                    // Try as JObject
                    var jsonObj = JObject.Parse(rawText);

                    // Look for common text fields
                    var textField = jsonObj["text"] ?? jsonObj["content"] ?? jsonObj["markdown"] ?? jsonObj["result"];
                    if (textField != null)
                    {
                        rawText = textField.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON, use as-is
                Log.Debug("Raw text is not JSON, using as-is");
            }

            // Clean up markdown formatting if present
            var cleanedText = rawText;

            // Remove markdown headers
            cleanedText = Regex.Replace(cleanedText, @"^#+\s*", "", RegexOptions.Multiline);

            // Remove markdown bold/italic
            cleanedText = Regex.Replace(cleanedText, @"\*\*([^*]+)\*\*", "$1");
            cleanedText = Regex.Replace(cleanedText, @"\*([^*]+)\*", "$1");
            cleanedText = Regex.Replace(cleanedText, @"__([^_]+)__", "$1");
            cleanedText = Regex.Replace(cleanedText, @"_([^_]+)_", "$1");

            // Remove markdown code blocks
            cleanedText = Regex.Replace(cleanedText, @"```[^`]*```", "", RegexOptions.Singleline);
            cleanedText = Regex.Replace(cleanedText, @"`([^`]+)`", "$1");

            // Remove excessive whitespace
            cleanedText = Regex.Replace(cleanedText, @"\s+", " ");

            return cleanedText.Trim();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
