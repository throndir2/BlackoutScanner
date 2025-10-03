using System.Threading.Tasks;

namespace BlackoutScanner.Interfaces
{
    /// <summary>
    /// Interface specific to NVIDIA Build AI service operations.
    /// </summary>
    public interface INvidiaAIService : IAIProvider
    {
        /// <summary>
        /// Gets or sets the API key for NVIDIA Build service.
        /// </summary>
        string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the model to use for OCR (e.g., "microsoft/kosmos-2", "nvidia/paddleocr").
        /// </summary>
        string Model { get; set; }

        /// <summary>
        /// Updates the service configuration with new API key and model.
        /// </summary>
        /// <param name="apiKey">The NVIDIA Build API key.</param>
        /// <param name="model">The model identifier to use.</param>
        void UpdateConfiguration(string apiKey, string model);
    }
}
