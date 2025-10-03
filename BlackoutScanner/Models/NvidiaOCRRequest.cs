using Newtonsoft.Json;
using System.Collections.Generic;

namespace BlackoutScanner.Models
{
    /// <summary>
    /// Represents the request payload for NVIDIA Build OCR API.
    /// </summary>
    public class NvidiaOCRRequest
    {
        [JsonProperty("input")]
        public List<NvidiaInputItem> Input { get; set; }

        public NvidiaOCRRequest()
        {
            Input = new List<NvidiaInputItem>();
        }
    }

    /// <summary>
    /// Represents an input item in the NVIDIA OCR request.
    /// </summary>
    public class NvidiaInputItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        public NvidiaInputItem()
        {
            Type = "image_url";
            Url = string.Empty;
        }
    }
}
