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
    /// Service for interacting with Google Gemini AI for OCR tasks.
    /// Supports gemini-2.5-flash and other Gemini models with vision capabilities.
    /// </summary>
    public class GeminiOCRService : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model;

        // Base URLs for Gemini API - different versions support different models
        private const string BaseUrlBeta = "https://generativelanguage.googleapis.com/v1beta/models";
        private const string BaseUrlAlpha = "https://generativelanguage.googleapis.com/v1alpha/models";
        
        // Models that require the v1alpha endpoint (preview/experimental models)
        private static readonly string[] AlphaModels = new[]
        {
            "gemini-3",           // All gemini-3 variants
            "gemini-2.5-pro",     // 2.5 pro preview
            "gemini-exp",         // Experimental models
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
4. Return ONLY the extracted text and confidence - no explanations or additional commentary

Be precise with unusual Unicode characters - these are real usernames and exact accuracy matters. Your response should be a single line for the text as defined by the json schema.";

        public string ProviderName => "Gemini";

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

        public GeminiOCRService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _apiKey = string.Empty;
            _model = "gemini-3-flash";
        }

        public void UpdateConfiguration(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
            Log.Information($"Gemini OCR Service configuration updated: Model={model}");
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_model);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                Log.Warning("Gemini OCR Service is not properly configured");
                return false;
            }

            try
            {
                // Create a minimal test request with a 1x1 transparent PNG
                var testImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

                var request = new GeminiRequest
                {
                    Contents = new List<GeminiContent>
                    {
                        new GeminiContent
                        {
                            Parts = new List<GeminiPart>
                            {
                                new GeminiPart
                                {
                                    InlineData = new GeminiInlineData
                                    {
                                        MimeType = "image/png",
                                        Data = testImageBase64
                                    }
                                },
                                new GeminiPart
                                {
                                    Text = "Test connection - just return 'OK'"
                                }
                            }
                        }
                    }
                };

                var response = await SendRequestAsync(request);

                Log.Information("Gemini OCR Service connection test successful");
                return response != null && response.Candidates != null && response.Candidates.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Gemini OCR Service connection test failed");
                return false;
            }
        }

        public async Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Gemini OCR Service is not properly configured. Please set API key and model.");
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

                var request = new GeminiRequest
                {
                    Contents = new List<GeminiContent>
                    {
                        new GeminiContent
                        {
                            Parts = new List<GeminiPart>
                            {
                                new GeminiPart
                                {
                                    InlineData = new GeminiInlineData
                                    {
                                        MimeType = mimeType,
                                        Data = imageBase64
                                    }
                                },
                                new GeminiPart
                                {
                                    Text = SystemPrompt
                                }
                            }
                        }
                    },
                    GenerationConfig = new GeminiGenerationConfig
                    {
                        ResponseMimeType = "application/json",
                        ResponseSchema = new GeminiResponseSchema
                        {
                            Type = "OBJECT",
                            Properties = new Dictionary<string, GeminiSchemaProperty>
                            {
                                { "text", new GeminiSchemaProperty { Type = "STRING" } },
                                { "confidence", new GeminiSchemaProperty { Type = "NUMBER" } }
                            },
                            Required = new List<string> { "text", "confidence" }
                        }
                    }
                };

                Log.Debug($"Sending OCR request to Gemini API: Model={_model}");
                var response = await SendRequestAsync(request);

                if (response == null)
                {
                    throw new Exception("Received null response from Gemini API");
                }

                // Extract text and confidence from JSON response
                var (extractedText, confidence) = ParseOCRResponse(response);
                Log.Information($"Gemini OCR completed successfully. Extracted {extractedText.Length} characters with {confidence:F2}% confidence.");

                return (extractedText, confidence);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to perform OCR using Gemini OCR Service");
                throw;
            }
        }

        /// <summary>
        /// Determines the appropriate API endpoint based on the model name.
        /// Newer/preview models use v1alpha, stable models use v1beta.
        /// </summary>
        private string GetApiBaseUrl()
        {
            // Check if the model requires the alpha endpoint
            foreach (var alphaModel in AlphaModels)
            {
                if (_model.StartsWith(alphaModel, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug($"Model '{_model}' requires v1alpha endpoint");
                    return BaseUrlAlpha;
                }
            }
            
            Log.Debug($"Model '{_model}' using v1beta endpoint");
            return BaseUrlBeta;
        }

        private async Task<GeminiResponse> SendRequestAsync(GeminiRequest request)
        {
            var baseUrl = GetApiBaseUrl();
            var url = $"{baseUrl}/{_model}:generateContent";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);

            // Set headers
            httpRequest.Headers.Add("x-goog-api-key", _apiKey);

            // Serialize request body
            var jsonContent = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            });
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);

            // Read response
            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error($"Gemini API request failed with status {httpResponse.StatusCode}: {responseContent}");
                throw new HttpRequestException($"Gemini API request failed: {httpResponse.StatusCode} - {responseContent}");
            }

            // Deserialize response
            var response = JsonConvert.DeserializeObject<GeminiResponse>(responseContent);
            return response ?? new GeminiResponse { Candidates = new List<GeminiCandidate>() };
        }

        private (string text, float confidence) ParseOCRResponse(GeminiResponse response)
        {
            if (response?.Candidates == null || response.Candidates.Count == 0)
            {
                Log.Warning("Gemini API response contains no candidates");
                return (string.Empty, 0f);
            }

            var candidate = response.Candidates[0];
            var contentText = candidate.Content?.Parts?[0]?.Text;

            if (string.IsNullOrWhiteSpace(contentText))
            {
                Log.Warning("Gemini API response contains no text content");
                return (string.Empty, 0f);
            }

            try
            {
                // Parse the JSON response from Gemini
                var ocrResult = JsonConvert.DeserializeObject<GeminiOCRResult>(contentText);

                if (ocrResult == null)
                {
                    Log.Warning($"Failed to parse Gemini OCR result from: {contentText}");
                    return (string.Empty, 0f);
                }

                var text = ocrResult.Text ?? string.Empty;
                var confidence = Math.Max(0f, Math.Min(100f, ocrResult.Confidence)); // Clamp to 0-100

                Log.Debug($"Parsed Gemini OCR result: Text='{text}', Confidence={confidence:F2}%");
                return (text, confidence);
            }
            catch (JsonException ex)
            {
                Log.Error(ex, $"Failed to parse Gemini JSON response: {contentText}");
                return (string.Empty, 0f);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #region Request/Response Models

        private class GeminiRequest
        {
            [JsonProperty("contents")]
            public List<GeminiContent> Contents { get; set; } = new();

            [JsonProperty("generationConfig")]
            public GeminiGenerationConfig? GenerationConfig { get; set; }
        }

        private class GeminiContent
        {
            [JsonProperty("parts")]
            public List<GeminiPart> Parts { get; set; } = new();
        }

        private class GeminiPart
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("inline_data")]
            public GeminiInlineData? InlineData { get; set; }
        }

        private class GeminiInlineData
        {
            [JsonProperty("mime_type")]
            public string MimeType { get; set; } = string.Empty;

            [JsonProperty("data")]
            public string Data { get; set; } = string.Empty;
        }

        private class GeminiGenerationConfig
        {
            [JsonProperty("responseMimeType")]
            public string ResponseMimeType { get; set; } = "application/json";

            [JsonProperty("responseSchema")]
            public GeminiResponseSchema? ResponseSchema { get; set; }
        }

        private class GeminiResponseSchema
        {
            [JsonProperty("type")]
            public string Type { get; set; } = "OBJECT";

            [JsonProperty("properties")]
            public Dictionary<string, GeminiSchemaProperty> Properties { get; set; } = new();

            [JsonProperty("required")]
            public List<string>? Required { get; set; }
        }

        private class GeminiSchemaProperty
        {
            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;
        }

        private class GeminiResponse
        {
            [JsonProperty("candidates")]
            public List<GeminiCandidate> Candidates { get; set; } = new();
        }

        private class GeminiCandidate
        {
            [JsonProperty("content")]
            public GeminiContent? Content { get; set; }

            [JsonProperty("finishReason")]
            public string? FinishReason { get; set; }
        }

        private class GeminiOCRResult
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("confidence")]
            public float Confidence { get; set; }
        }

        #endregion
    }
}
