using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace BlackoutScanner
{
    public partial class WindowSearchDialog : Window
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public class WindowInfo
        {
            public string Title { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public IntPtr Handle { get; set; }
        }

        private ObservableCollection<WindowInfo> allWindows = new ObservableCollection<WindowInfo>();
        private ICollectionView windowsView;

        public string? SelectedWindowTitle { get; private set; }

        public WindowSearchDialog()
        {
            InitializeComponent();
            LoadWindows();

            windowsView = CollectionViewSource.GetDefaultView(allWindows);
            windowsView.Filter = FilterWindows;
            windowsDataGrid.ItemsSource = windowsView;
        }

        private void LoadWindows()
        {
            allWindows.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        StringBuilder sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            // Get process name
                            GetWindowThreadProcessId(hWnd, out uint processId);
                            string processName = string.Empty;
                            try
                            {
                                Process process = Process.GetProcessById((int)processId);
                                processName = process.ProcessName;
                            }
                            catch
                            {
                                processName = "Unknown";
                            }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                allWindows.Add(new WindowInfo
                                {
                                    Title = title,
                                    ProcessName = processName,
                                    Handle = hWnd
                                });
                            });
                        }
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
        }

        private bool FilterWindows(object item)
        {
            if (string.IsNullOrWhiteSpace(filterTextBox.Text))
                return true;

            WindowInfo window = (WindowInfo)item;
            string filter = filterTextBox.Text.ToLower();
            return window.Title.ToLower().Contains(filter) ||
                   window.ProcessName.ToLower().Contains(filter);
        }

        private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            windowsView?.Refresh();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            SelectWindow();
        }

        private void WindowsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectWindow();
        }

        private void SelectWindow()
        {
            if (windowsDataGrid.SelectedItem is WindowInfo selectedWindow)
            {
                SelectedWindowTitle = selectedWindow.Title;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a window from the list.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
