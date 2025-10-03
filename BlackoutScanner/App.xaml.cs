using Serilog;
using Serilog.Events;
using System;
using System.Windows;
using BlackoutScanner.Infrastructure;
using BlackoutScanner.Interfaces;
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

                // Configure ServiceLocator BEFORE creating MainWindow
                // This ensures all services (especially OCRProcessor) are ready before MainWindow is created
                Log.Information("=== APPLICATION STARTUP: Configuring ServiceLocator ===");
                ServiceLocator.Configure();
                Log.Information("=== APPLICATION STARTUP: ServiceLocator configured successfully ===");

                SetProcessDpiAwareness();

                // NOW create and show MainWindow AFTER all services are initialized
                Log.Information("=== APPLICATION STARTUP: Creating MainWindow ===");
                var mainWindow = new Views.MainWindow();
                mainWindow.Show();
                Log.Information("=== APPLICATION STARTUP: MainWindow created and shown ===");

                // Note: OCR initialization will happen in MainWindow.InitializeAppComponents()
                // after the UI is shown, to prevent blocking the UI thread
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
            try
            {
                Log.Information("Application exiting...");

                // Flush and close Serilog
                // Note: MainWindow.OnClosed also calls this, but it's safe to call multiple times
                Log.CloseAndFlush();
            }
            catch
            {
                // Ignore errors during shutdown
            }

            base.OnExit(e);
        }
    }
}
