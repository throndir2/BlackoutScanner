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
    /// Service for interacting with Mistral AI API for OCR tasks.
    /// Supports vision-capable models with OpenAI-compatible API format.
    /// </summary>
    public class MistralOCRService : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URL for Mistral API
        private const string BaseUrl = "https://api.mistral.ai/v1/chat/completions";

        // Available models for Mistral vision OCR (models with vision capabilities)
        public static readonly string[] AvailableModels = new[]
        {
            "mistral-large-latest",                    // Default - Mistral Large 3 (latest)
            "pixtral-12b-2409",                        // Pixtral 12B - dedicated vision model
            "mistral-small-latest",                    // Mistral Small 3.2 (latest)
            "mistral-medium-latest",                   // Mistral Medium 3.1 (latest)
            "ministral-8b-latest",                     // Ministral 3 8B (latest)
            "ministral-3b-latest",                     // Ministral 3 3B (latest)
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

        public string ProviderName => "Mistral";

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

        public MistralOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _apiKey = string.Empty;
            _model = "mistral-large-latest"; // Default to mistral-large-latest
        }

        public void UpdateConfiguration(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
            Log.Information($"Mistral OCR Service configuration updated: Model={model}");
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                Log.Warning("Mistral OCR Service is not properly configured");
                return false;
            }

            try
            {
                // Create a minimal test request with a 2x2 red PNG
                var testImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAADklEQVR4AWP4z8DAwMAAAw4B/xnvKSEAAAAASUVORK5CYII=";

                var response = await SendRequestAsync(testImageBase64, "image/png", "Test connection - just return 'OK'");

                Log.Information("Mistral OCR Service connection test successful");
                return response != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Mistral OCR Service connection test failed");
                return false;
            }
        }

        public async Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Mistral OCR Service is not properly configured. Please set API key and model.");
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

                Log.Debug($"Sending OCR request to Mistral API: Model={_model}");
                var response = await SendRequestAsync(imageBase64, mimeType, SystemPrompt);

                if (response == null)
                {
                    throw new Exception("Received null response from Mistral API");
                }

                // Extract text and confidence from JSON response
                var (extractedText, confidence) = ParseOCRResponse(response);
                Log.Information($"Mistral OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

                return (extractedText, confidence);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using Mistral OCR Service");
                throw;
            }
        }

        private async Task<MistralResponse?> SendRequestAsync(string imageBase64, string mimeType, string prompt)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);

            // Set headers
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            // Build the request payload (Mistral uses similar format to OpenAI)
            var requestPayload = new
            {
                model = _model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = "You are an OCR assistant. Extract text from images and return results as JSON."
                            }
                        }
                    },
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
                                type = "image_url",
                                image_url = $"data:{mimeType};base64,{imageBase64}"
                            }
                        }
                    }
                },
                response_format = new { type = "json_object" },
                temperature = 0.1,  // Low temperature for more deterministic OCR
                max_tokens = 1024
            };

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(requestPayload);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Log.Debug($"Mistral request payload: model={_model}");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            // Log rate limit headers for debugging
            LogRateLimitHeaders(httpResponse);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"Mistral API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"Mistral API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            Log.Debug($"Mistral raw response: {responseContent}");

            // Deserialize response
            var response = JsonConvert.DeserializeObject<MistralResponse>(responseContent);
            return response;
        }

        private void LogRateLimitHeaders(HttpResponseMessage response)
        {
            // Log rate limit information from headers
            if (response.Headers.TryGetValues("x-ratelimit-remaining-requests", out var remainingRequests))
            {
                Log.Debug($"[Mistral RateLimit] Remaining requests: {string.Join(",", remainingRequests)}");
            }
            if (response.Headers.TryGetValues("x-ratelimit-remaining-tokens", out var remainingTokens))
            {
                Log.Debug($"[Mistral RateLimit] Remaining tokens: {string.Join(",", remainingTokens)}");
            }
            if (response.Headers.TryGetValues("retry-after", out var retryAfter))
            {
                Log.Warning($"[Mistral RateLimit] Rate limited! Retry after: {string.Join(",", retryAfter)} seconds");
            }
        }

        private (string text, float confidence) ParseOCRResponse(MistralResponse response)
        {
            if (response?.Choices == null || response.Choices.Count == 0)
            {
                Log.Warning("Mistral API response contains no choices");
                return (string.Empty, 0f);
            }

            var choice = response.Choices[0];
            var contentText = choice.Message?.Content;

            if (string.IsNullOrWhiteSpace(contentText))
            {
                Log.Warning("Mistral API response contains no text content");
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

                var ocrResult = JsonConvert.DeserializeObject<MistralOCRResult>(cleanedContent);

                if (ocrResult == null)
                {
                    Log.Warning($"Failed to parse Mistral OCR result from: {contentText}");
                    // Fall back to using raw content as text with lower confidence
                    return (contentText.Trim(), 70f);
                }

                var text = ocrResult.Text ?? string.Empty;
                var confidence = Math.Max(0f, Math.Min(100f, ocrResult.Confidence)); // Clamp to 0-100

                Log.Debug($"Parsed Mistral OCR result: Text='{text}', Confidence={confidence:F2}%");
                return (text, confidence);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, $"Failed to parse Mistral JSON response, using raw text: {contentText}");
                // Fall back to using raw content as text with lower confidence
                return (contentText.Trim(), 70f);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #region Request/Response Models

        private class MistralResponse
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("object")]
            public string? Object { get; set; }

            [JsonProperty("created")]
            public long Created { get; set; }

            [JsonProperty("model")]
            public string? Model { get; set; }

            [JsonProperty("choices")]
            public List<MistralChoice>? Choices { get; set; }

            [JsonProperty("usage")]
            public MistralUsage? Usage { get; set; }
        }

        private class MistralChoice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public MistralMessage? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class MistralMessage
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private class MistralUsage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }

        private class MistralOCRResult
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("confidence")]
            public float Confidence { get; set; }
        }

        #endregion
    }
}
