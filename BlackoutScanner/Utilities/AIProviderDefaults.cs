using System;
using System.Collections.Generic;

namespace BlackoutScanner.Utilities
{
    /// <summary>
    /// Provides default configuration values for AI providers including rate limits.
    /// </summary>
    public static class AIProviderDefaults
    {
        /// <summary>
        /// Default requests per minute for known models (Free tier limits).
        /// </summary>
        private static readonly Dictionary<string, int> DefaultRateLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // NVIDIA Build API (applies to all models)
            { "nvidia", 40 },
            { "baidu/paddleocr", 40 },
            { "nvidia/nemoretriever-ocr-v1", 40 },
            
            // Google Gemini Models (Free tier)
            { "gemini-2.5-pro", 5 },
            { "gemini-2.5-flash", 10 },
            { "gemini-2.5-flash-preview", 10 },
            { "gemini-2.5-flash-lite", 15 },
            { "gemini-2.5-flash-lite-preview", 15 },
            { "gemini-2.0-flash", 15 },
            { "gemini-2.0-flash-lite", 30 },
            { "gemini-1.5-pro", 2 },
            { "gemini-1.5-flash", 15 },
            { "gemini-pro-vision", 60 },
            
            // OpenAI (Conservative defaults for free tier)
            { "gpt-4o", 3 },
            { "gpt-4-turbo", 3 },
            { "gpt-4-vision-preview", 3 },
        };

        /// <summary>
        /// Gets the default requests per minute for a given provider type and model.
        /// </summary>
        /// <param name="providerType">The provider type (e.g., "NvidiaBuild", "Gemini", "OpenAI")</param>
        /// <param name="model">The specific model name</param>
        /// <returns>Default RPM, or 10 if no specific default is found</returns>
        public static int GetDefaultRequestsPerMinute(string providerType, string model)
        {
            if (string.IsNullOrEmpty(model))
            {
                return GetDefaultForProviderType(providerType);
            }

            // Try exact model match first
            if (DefaultRateLimits.TryGetValue(model, out int rpm))
            {
                return rpm;
            }

            // Try provider type match
            if (!string.IsNullOrEmpty(providerType))
            {
                return GetDefaultForProviderType(providerType);
            }

            // Conservative fallback
            return 10;
        }

        /// <summary>
        /// Gets the default RPM based on provider type alone.
        /// </summary>
        private static int GetDefaultForProviderType(string providerType)
        {
            return providerType?.ToLowerInvariant() switch
            {
                "nvidiabuild" => 40,
                "gemini" => 15, // Default to Gemini 2.0 Flash / 1.5 Flash tier
                "openai" => 3,  // Conservative default for OpenAI
                _ => 10         // Generic fallback
            };
        }

        /// <summary>
        /// Gets suggested model names for a given provider type.
        /// </summary>
        public static List<string> GetSuggestedModels(string providerType)
        {
            return providerType?.ToLowerInvariant() switch
            {
                "nvidiabuild" => new List<string> { "baidu/paddleocr", "nvidia/nemoretriever-ocr-v1" },
                "gemini" => new List<string>
                {
                    "gemini-2.5-flash",
                    "gemini-2.0-flash",
                    "gemini-1.5-flash",
                    "gemini-1.5-pro",
                    "gemini-2.5-pro",
                    "gemini-pro-vision"
                },
                "openai" => new List<string> { "gpt-4o", "gpt-4-turbo", "gpt-4-vision-preview" },
                _ => new List<string>()
            };
        }
    }
}
