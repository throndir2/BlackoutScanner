using System.Threading.Tasks;

namespace BlackoutScanner.Interfaces
{
    /// <summary>
    /// Interface specific to NVIDIA Build AI OCR service operations.
    /// Supports PaddleOCR and NeMo Retriever OCR models.
    /// </summary>
    public interface INvidiaOCRService : IAIProvider
    {
        /// <summary>
        /// Gets or sets the API key for NVIDIA Build service.
        /// </summary>
        string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the OCR model to use (e.g., "baidu/paddleocr", "nvidia/nemoretriever-ocr-v1").
        /// </summary>
        string Model { get; set; }

        /// <summary>
        /// Updates the service configuration with new API key and model.
        /// </summary>
        /// <param name="apiKey">The NVIDIA Build API key.</param>
        /// <param name="model">The OCR model identifier to use.</param>
        void UpdateConfiguration(string apiKey, string model);
    }
}
