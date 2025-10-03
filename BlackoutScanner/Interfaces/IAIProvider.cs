using System.Threading.Tasks;

namespace BlackoutScanner.Interfaces
{
    /// <summary>
    /// Generic interface for AI service providers that can enhance OCR results.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Gets the name of the AI provider (e.g., "NvidiaBuild", "OpenAI", etc.).
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Validates that the provider is properly configured with necessary credentials.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise.</returns>
        bool IsConfigured();

        /// <summary>
        /// Tests the connection to the AI service.
        /// </summary>
        /// <returns>True if connection is successful, false otherwise.</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Performs OCR on the provided image data using the AI service.
        /// </summary>
        /// <param name="imageData">The image data as a byte array.</param>
        /// <returns>A tuple containing the OCR result text and confidence percentage (0-100).</returns>
        Task<(string text, float confidence)> PerformOCRAsync(byte[] imageData);
    }
}
