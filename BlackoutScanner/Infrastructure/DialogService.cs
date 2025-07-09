using System;
using System.Drawing;
using System.Windows;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Views;
using Microsoft.Win32;

namespace BlackoutScanner.Infrastructure
{
    public class DialogService : IDialogService
    {
        public void ShowError(string message, string title = "Error")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void ShowInfo(string message, string title = "Information")
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public bool ShowConfirmation(string message, string title = "Confirm")
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public string? ShowSaveFileDialog(string filter, string defaultFileName)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = defaultFileName,
                RestoreDirectory = true
            };

            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
        }

        public string? ShowOpenFileDialog(string filter)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = filter,
                RestoreDirectory = true
            };

            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
        }

        public Rectangle? ShowAreaSelector(Rectangle gameWindowRect)
        {
            var areaSelectorWindow = new AreaSelectorWindow(gameWindowRect);
            bool? result = areaSelectorWindow.ShowDialog();

            return result == true ? areaSelectorWindow.SelectedRectangle : null;
        }

        public string? ShowWindowSearch()
        {
            var windowSearchDialog = new WindowSearchDialog();
            bool? result = windowSearchDialog.ShowDialog();

            return result == true ? windowSearchDialog.SelectedWindowTitle : null;
        }
    }
}
