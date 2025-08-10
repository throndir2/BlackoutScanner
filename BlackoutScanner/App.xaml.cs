using Serilog;
using Serilog.Events;
using System;
using System.Windows;
using BlackoutScanner.Infrastructure;
using BlackoutScanner.Utilities;

namespace BlackoutScanner
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Configure Serilog with UI sink BEFORE ServiceLocator
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // Log everything to file
                .WriteTo.File("logs/blackoutscanner-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Verbose) // Everything to file
                .WriteTo.UI() // UI sink will filter based on UISink.MinimumLevel
                .CreateLogger();

            // Set default UI log level
            UISink.MinimumLevel = LogEventLevel.Information;

            try
            {
                base.OnStartup(e);

                // Configure ServiceLocator after Serilog
                ServiceLocator.Configure();

                SetProcessDpiAwareness();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                MessageBox.Show($"Failed to start: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
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
            // Flush and close Serilog
            Log.CloseAndFlush();

            base.OnExit(e);
        }
    }
}
