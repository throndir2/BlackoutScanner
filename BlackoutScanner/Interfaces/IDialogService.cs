using System.Drawing;

namespace BlackoutScanner.Interfaces
{
    public interface IDialogService
    {
        void ShowError(string message, string title = "Error");
        void ShowInfo(string message, string title = "Information");
        bool ShowConfirmation(string message, string title = "Confirm");
        string? ShowSaveFileDialog(string filter, string defaultFileName);
        string? ShowOpenFileDialog(string filter);
        Rectangle? ShowAreaSelector(Rectangle gameWindowRect);
        string? ShowWindowSearch();
    }
}
