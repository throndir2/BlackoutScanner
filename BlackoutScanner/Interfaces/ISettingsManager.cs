using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface ISettingsManager
    {
        AppSettings Settings { get; }
        void SaveSettings();
        void LoadSettings();
        string GetFullExportPath();
    }

}
