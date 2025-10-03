using BlackoutScanner.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BlackoutScanner.Views
{
    /// <summary>
    /// Interaction logic for AIQueueMonitorView.xaml
    /// </summary>
    public partial class AIQueueMonitorView : UserControl
    {
        public AIQueueMonitorView()
        {
            InitializeComponent();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AIQueueMonitorViewModel viewModel)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear all processed items?",
                    "Clear History",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    viewModel.ClearProcessed();
                }
            }
        }
    }
}
