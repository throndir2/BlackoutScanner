using Newtonsoft.Json;
using System.Collections.Generic;

namespace BlackoutScanner.Models
{
    /// <summary>
    /// Represents the response from NVIDIA Build OCR API.
    /// </summary>
    public class NvidiaOCRResponse
    {
        [JsonProperty("data")]
        public List<NvidiaOCRData> Data { get; set; }

        [JsonProperty("error")]
        public NvidiaError? Error { get; set; }

        public NvidiaOCRResponse()
        {
            Data = new List<NvidiaOCRData>();
        }

        /// <summary>
        /// Checks if the response contains an error.
        /// </summary>
        public bool HasError => Error != null;
    }

    /// <summary>
    /// Represents OCR data from the NVIDIA response.
    /// Supports both simple format (content/type) and PaddleOCR format (text_detections).
    /// </summary>
    public class NvidiaOCRData
    {
        // Simple format (used by some models)
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        // PaddleOCR format (used by paddleocr and nemoretriever-ocr-v1)
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("text_detections")]
        public List<TextDetection>? TextDetections { get; set; }

        public NvidiaOCRData()
        {
            Content = string.Empty;
            Type = string.Empty;
        }
    }

    /// <summary>
    /// Represents a text detection in the PaddleOCR format.
    /// </summary>
    public class TextDetection
    {
        [JsonProperty("text_prediction")]
        public TextPrediction TextPrediction { get; set; }

        [JsonProperty("bounding_box")]
        public BoundingBox BoundingBox { get; set; }

        public TextDetection()
        {
            TextPrediction = new TextPrediction();
            BoundingBox = new BoundingBox();
        }
    }

    /// <summary>
    /// Represents predicted text and confidence.
    /// </summary>
    public class TextPrediction
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("confidence")]
        public float Confidence { get; set; }

        public TextPrediction()
        {
            Text = string.Empty;
        }
    }

    /// <summary>
    /// Represents bounding box coordinates for detected text.
    /// </summary>
    public class BoundingBox
    {
        [JsonProperty("points")]
        public List<Point> Points { get; set; }

        public BoundingBox()
        {
            Points = new List<Point>();
        }
    }

    /// <summary>
    /// Represents a point in the bounding box.
    /// </summary>
    public class Point
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }
    }

    /// <summary>
    /// Represents an error from the NVIDIA API.
    /// </summary>
    public class NvidiaError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        public NvidiaError()
        {
            Message = string.Empty;
            Code = string.Empty;
        }
    }
}
