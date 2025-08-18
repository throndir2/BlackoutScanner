using System.Collections.Generic;
using System.Linq;

namespace BlackoutScanner.Models
{
    public class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public static class LanguageHelper
    {
        public static readonly List<LanguageInfo> AvailableLanguages = new List<LanguageInfo>
        {
            // Latin script
            new LanguageInfo { Code = "eng", DisplayName = "English", Script = "Latin" },
            new LanguageInfo { Code = "spa", DisplayName = "Spanish", Script = "Latin" },
            new LanguageInfo { Code = "fra", DisplayName = "French", Script = "Latin" },
            new LanguageInfo { Code = "deu", DisplayName = "German", Script = "Latin" },
            new LanguageInfo { Code = "ita", DisplayName = "Italian", Script = "Latin" },
            new LanguageInfo { Code = "por", DisplayName = "Portuguese", Script = "Latin" },
            new LanguageInfo { Code = "pol", DisplayName = "Polish", Script = "Latin" },
            new LanguageInfo { Code = "tur", DisplayName = "Turkish", Script = "Latin" },
            new LanguageInfo { Code = "vie", DisplayName = "Vietnamese", Script = "Latin" },
            
            // Cyrillic script
            new LanguageInfo { Code = "rus", DisplayName = "Russian", Script = "Cyrillic" },
            new LanguageInfo { Code = "ukr", DisplayName = "Ukrainian", Script = "Cyrillic" },
            
            // CJK scripts
            new LanguageInfo { Code = "jpn", DisplayName = "Japanese", Script = "Japanese" },
            new LanguageInfo { Code = "kor", DisplayName = "Korean", Script = "Korean" },
            new LanguageInfo { Code = "chi_sim", DisplayName = "Chinese (Simplified)", Script = "Chinese" },
            new LanguageInfo { Code = "chi_tra", DisplayName = "Chinese (Traditional)", Script = "Chinese" },
            
            // Arabic script
            new LanguageInfo { Code = "ara", DisplayName = "Arabic", Script = "Arabic" },
            
            // Devanagari script
            new LanguageInfo { Code = "hin", DisplayName = "Hindi", Script = "Devanagari" },
            
            // Greek script
            new LanguageInfo { Code = "ell", DisplayName = "Greek", Script = "Greek" },
            
            // Hebrew script
            new LanguageInfo { Code = "heb", DisplayName = "Hebrew", Script = "Hebrew" },
            
            // Thai script
            new LanguageInfo { Code = "tha", DisplayName = "Thai", Script = "Thai" }
        };

        public static Dictionary<string, List<LanguageInfo>> GetLanguagesByScript()
        {
            return AvailableLanguages.GroupBy(l => l.Script)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}
