using Serilog;
using System;
using System.Windows;

namespace BlackoutScanner
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetProcessDpiAwareness();
        }

        private void SetProcessDpiAwareness()
        {
            try
            {
                // This makes the application DPI aware
                var dpiAwareness = DpiHelper.SetProcessDpiAwareness(DpiHelper.PROCESS_DPI_AWARENESS.Process_Per_Monitor_DPI_Aware);
                Log.Information($"DPI Awareness set to: {dpiAwareness}");
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not set DPI awareness: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}
