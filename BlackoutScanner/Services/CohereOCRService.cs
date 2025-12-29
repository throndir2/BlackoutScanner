using BlackoutScanner.Interfaces;
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
    /// Service for interacting with Cohere API for OCR tasks.
    /// Supports Command A Vision model for image understanding and OCR.
    /// </summary>
    public class CohereOCRService : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URL for Cohere API
        private const string BaseUrl = "https://api.cohere.ai/v2/chat";

        // Available models for Cohere vision OCR
        // Only Command A Vision supports image understanding for OCR
        public static readonly string[] AvailableModels = new[]
        {
            "command-a-vision-07-2025",  // Command A Vision for OCR/image tasks (128K context, 8K output)
        };

        // System prompt for OCR with special character handling
        private const string SystemPrompt = @"Extract all visible text from this image using OCR. 
This text may contain unique usernames or names with special characters, superscripts, subscripts, and mixed-language characters.
Examples of valid text patterns you might encounter:
- Minatõ (with tilde over o)
- Danteᵗᵃˢ (with superscript letters)
- ᶜᶜSakamoto (with superscript c)
- •OldMan• (with bullet points)
- BRZRKR웃 (mixing Latin and Korean characters)
- Names with emoji, symbols, or Unicode decorations

Your task:
1. Identify ALL visible text in the image, preserving exact character representations
2. Match special characters, diacritics, superscripts, subscripts, and symbols precisely
3. Provide a confidence score (0-100) indicating how certain you are about the OCR accuracy
4. Return ONLY the extracted text and confidence as JSON - no explanations or additional commentary

Be precise with unusual Unicode characters - these are real usernames and exact accuracy matters.

Respond ONLY with valid JSON in this exact format:
{""text"": ""extracted text here"", ""confidence"": 95}";

        public string ProviderName => "Cohere";

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

        public CohereOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _apiKey = string.Empty;
            _model = "command-a-vision-07-2025"; // Default to Command A Vision
        }

        public void UpdateConfiguration(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
            Log.Information($"Cohere OCR Service configuration updated: Model={model}");
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                Log.Warning("Cohere OCR Service is not properly configured");
                return false;
            }

            try
            {
                // Create a minimal test request with a 2x2 red PNG
                var testImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAADklEQVR4AWP4z8DAwMAAAw4B/xnvKSEAAAAASUVORK5CYII=";

                var response = await SendRequestAsync(testImageBase64, "image/png", "Test connection - just return 'OK'");

                Log.Information("Cohere OCR Service connection test successful");
                return response != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cohere OCR Service connection test failed");
                return false;
            }
        }

        public async Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Cohere OCR Service is not properly configured. Please set API key and model.");
            }

            if (imageData == null || imageData.Length == 0)
            {
                throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));
            }

            try
            {
                // Convert image to base64
                var imageBase64 = Convert.ToBase64String(imageData);

                // Determine MIME type (assume PNG for now, could be enhanced)
                var mimeType = "image/png";

                Log.Debug($"Sending OCR request to Cohere API: Model={_model}");
                var response = await SendRequestAsync(imageBase64, mimeType, SystemPrompt);

                if (response == null)
                {
                    throw new Exception("Received null response from Cohere API");
                }

                // Extract text and confidence from JSON response
                var (extractedText, confidence) = ParseOCRResponse(response);
                Log.Information($"Cohere OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

                return (extractedText, confidence);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using Cohere OCR Service");
                throw;
            }
        }

        private async Task<CohereResponse?> SendRequestAsync(string imageBase64, string mimeType, string prompt)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);

            // Set headers - Cohere uses Bearer token authorization
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            // Build the request payload for Cohere v2 chat API with vision
            // Cohere v2 expects image as a data URL string in the format: data:{mime_type};base64,{data}
            var imageDataUrl = $"data:{mimeType};base64,{imageBase64}";
            
            var requestPayload = new
            {
                model = _model,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = prompt
                            },
                            new
                            {
                                type = "image",
                                image = imageDataUrl  // Data URL string format
                            }
                        }
                    }
                },
                temperature = 0.1  // Low temperature for more deterministic OCR
            };

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(requestPayload);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Log.Debug($"Cohere request payload: model={_model}");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            // Log rate limit headers for debugging
            LogRateLimitHeaders(httpResponse);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"Cohere API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"Cohere API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            Log.Debug($"Cohere raw response: {responseContent}");

            // Deserialize response
            var response = JsonConvert.DeserializeObject<CohereResponse>(responseContent);
            return response;
        }

        private void LogRateLimitHeaders(HttpResponseMessage response)
        {
            // Log rate limit information from headers
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining))
            {
                Log.Debug($"[Cohere RateLimit] Remaining requests: {string.Join(",", remaining)}");
            }
            if (response.Headers.TryGetValues("x-ratelimit-limit", out var limit))
            {
                Log.Debug($"[Cohere RateLimit] Request limit: {string.Join(",", limit)}");
            }
            if (response.Headers.TryGetValues("retry-after", out var retryAfter))
            {
                Log.Warning($"[Cohere RateLimit] Rate limited! Retry after: {string.Join(",", retryAfter)} seconds");
            }
        }

        private (string text, float confidence) ParseOCRResponse(CohereResponse response)
        {
            // Cohere v2 response format has message.content array
            if (response?.Message?.Content == null || response.Message.Content.Count == 0)
            {
                Log.Warning("Cohere API response contains no content");
                return (string.Empty, 0f);
            }

            // Find the text content in the response
            string? contentText = null;
            foreach (var content in response.Message.Content)
            {
                if (content.Type == "text" && !string.IsNullOrWhiteSpace(content.Text))
                {
                    contentText = content.Text;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(contentText))
            {
                Log.Warning("Cohere API response contains no text content");
                return (string.Empty, 0f);
            }

            try
            {
                // Try to parse as JSON response
                // Clean up the response - sometimes models wrap in markdown code blocks
                var cleanedContent = contentText.Trim();
                if (cleanedContent.StartsWith("```json"))
                {
                    cleanedContent = cleanedContent.Substring(7);
                }
                if (cleanedContent.StartsWith("```"))
                {
                    cleanedContent = cleanedContent.Substring(3);
                }
                if (cleanedContent.EndsWith("```"))
                {
                    cleanedContent = cleanedContent.Substring(0, cleanedContent.Length - 3);
                }
                cleanedContent = cleanedContent.Trim();

                var ocrResult = JsonConvert.DeserializeObject<CohereOCRResult>(cleanedContent);

                if (ocrResult == null)
                {
                    Log.Warning($"Failed to parse Cohere OCR result from: {contentText}");
                    // Fall back to using raw content as text with lower confidence
                    return (contentText.Trim(), 70f);
                }

                var text = ocrResult.Text ?? string.Empty;
                var confidence = Math.Max(0f, Math.Min(100f, ocrResult.Confidence)); // Clamp to 0-100

                Log.Debug($"Parsed Cohere OCR result: Text='{text}', Confidence={confidence:F2}%");
                return (text, confidence);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, $"Failed to parse Cohere JSON response, using raw text: {contentText}");
                // Fall back to using raw content as text with lower confidence
                return (contentText.Trim(), 70f);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #region Request/Response Models

        private class CohereResponse
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }

            [JsonProperty("message")]
            public CohereMessage? Message { get; set; }

            [JsonProperty("usage")]
            public CohereUsage? Usage { get; set; }
        }

        private class CohereMessage
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public List<CohereContent>? Content { get; set; }
        }

        private class CohereContent
        {
            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("text")]
            public string? Text { get; set; }
        }

        private class CohereUsage
        {
            [JsonProperty("billed_units")]
            public CohereBilledUnits? BilledUnits { get; set; }

            [JsonProperty("tokens")]
            public CohereTokens? Tokens { get; set; }
        }

        private class CohereBilledUnits
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }

            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }

        private class CohereTokens
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }

            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }

        private class CohereOCRResult
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("confidence")]
            public float Confidence { get; set; }
        }

        #endregion
    }
}
