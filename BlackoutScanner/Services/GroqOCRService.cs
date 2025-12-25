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
    /// Service for interacting with GroqCloud API for OCR tasks.
    /// Supports Llama 4 vision models with OpenAI-compatible API format.
    /// </summary>
    public class GroqOCRService : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URL for Groq API (OpenAI-compatible endpoint)
        private const string BaseUrl = "https://api.groq.com/openai/v1/chat/completions";

        // Available models for Groq vision OCR
        public static readonly string[] AvailableModels = new[]
        {
            "meta-llama/llama-4-maverick-17b-128e-instruct",  // Default - larger context
            "meta-llama/llama-4-scout-17b-16e-instruct"       // Faster, smaller context
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

        public string ProviderName => "Groq";

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

        public GroqOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _apiKey = string.Empty;
            _model = "meta-llama/llama-4-maverick-17b-128e-instruct"; // Default to maverick
        }

        public void UpdateConfiguration(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
            Log.Information($"Groq OCR Service configuration updated: Model={model}");
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                Log.Warning("Groq OCR Service is not properly configured");
                return false;
            }

            try
            {
                // Create a minimal test request with a 2x2 red PNG (Groq requires at least 2 pixels per dimension)
                var testImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAADklEQVR4AWP4z8DAwMAAAw4B/xnvKSEAAAAASUVORK5CYII=";

                var response = await SendRequestAsync(testImageBase64, "image/png", "Test connection - just return 'OK'");

                Log.Information("Groq OCR Service connection test successful");
                return response != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Groq OCR Service connection test failed");
                return false;
            }
        }

        public async Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Groq OCR Service is not properly configured. Please set API key and model.");
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

                Log.Debug($"Sending OCR request to Groq API: Model={_model}");
                var response = await SendRequestAsync(imageBase64, mimeType, SystemPrompt);

                if (response == null)
                {
                    throw new Exception("Received null response from Groq API");
                }

                // Extract text and confidence from JSON response
                var (extractedText, confidence) = ParseOCRResponse(response);
                Log.Information($"Groq OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

                return (extractedText, confidence);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using Groq OCR Service");
                throw;
            }
        }

        private async Task<GroqResponse?> SendRequestAsync(string imageBase64, string mimeType, string prompt)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);

            // Set headers
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            // Build the request payload (OpenAI-compatible format)
            var requestPayload = new
            {
                model = _model,
                messages = new[]
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
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{mimeType};base64,{imageBase64}"
                                }
                            }
                        }
                    }
                },
                temperature = 0.1,  // Low temperature for more deterministic OCR
                max_completion_tokens = 1024,
                top_p = 1,
                stream = false
            };

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(requestPayload);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Log.Debug($"Groq request payload: model={_model}");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            // Log rate limit headers for debugging
            LogRateLimitHeaders(httpResponse);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"Groq API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"Groq API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            Log.Debug($"Groq raw response: {responseContent}");

            // Deserialize response
            var response = JsonConvert.DeserializeObject<GroqResponse>(responseContent);
            return response;
        }

        private void LogRateLimitHeaders(HttpResponseMessage response)
        {
            // Log rate limit information from headers
            if (response.Headers.TryGetValues("x-ratelimit-remaining-requests", out var remainingRequests))
            {
                Log.Debug($"[Groq RateLimit] Remaining requests (daily): {string.Join(",", remainingRequests)}");
            }
            if (response.Headers.TryGetValues("x-ratelimit-remaining-tokens", out var remainingTokens))
            {
                Log.Debug($"[Groq RateLimit] Remaining tokens (per minute): {string.Join(",", remainingTokens)}");
            }
            if (response.Headers.TryGetValues("retry-after", out var retryAfter))
            {
                Log.Warning($"[Groq RateLimit] Rate limited! Retry after: {string.Join(",", retryAfter)} seconds");
            }
        }

        private (string text, float confidence) ParseOCRResponse(GroqResponse response)
        {
            if (response?.Choices == null || response.Choices.Count == 0)
            {
                Log.Warning("Groq API response contains no choices");
                return (string.Empty, 0f);
            }

            var choice = response.Choices[0];
            var contentText = choice.Message?.Content;

            if (string.IsNullOrWhiteSpace(contentText))
            {
                Log.Warning("Groq API response contains no text content");
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

                var ocrResult = JsonConvert.DeserializeObject<GroqOCRResult>(cleanedContent);

                if (ocrResult == null)
                {
                    Log.Warning($"Failed to parse Groq OCR result from: {contentText}");
                    // Fall back to using raw content as text with lower confidence
                    return (contentText.Trim(), 70f);
                }

                var text = ocrResult.Text ?? string.Empty;
                var confidence = Math.Max(0f, Math.Min(100f, ocrResult.Confidence)); // Clamp to 0-100

                Log.Debug($"Parsed Groq OCR result: Text='{text}', Confidence={confidence:F2}%");
                return (text, confidence);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, $"Failed to parse Groq JSON response, using raw text: {contentText}");
                // Fall back to using raw content as text with lower confidence
                return (contentText.Trim(), 70f);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #region Request/Response Models

        private class GroqResponse
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
            public List<GroqChoice>? Choices { get; set; }

            [JsonProperty("usage")]
            public GroqUsage? Usage { get; set; }
        }

        private class GroqChoice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public GroqMessage? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class GroqMessage
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private class GroqUsage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }

        private class GroqOCRResult
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("confidence")]
            public float Confidence { get; set; }
        }

        #endregion
    }
}
