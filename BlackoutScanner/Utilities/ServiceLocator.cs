using System;
using System.Windows;
using System.Windows.Threading;
using BlackoutScanner.Infrastructure;
using BlackoutScanner.Interfaces;
using BlackoutScanner.Repositories;
using BlackoutScanner.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BlackoutScanner.Utilities
{
    public static class ServiceLocator
    {
        private static IServiceProvider? _serviceProvider;

        public static bool IsInitialized => _serviceProvider != null;

        public static void Configure()
        {
            var services = new ServiceCollection();

            // Use the already-configured global Serilog logger from App.xaml.cs
            // DO NOT create a new logger here - it would miss the UI sink!
            services.AddSingleton(Log.Logger);

            // Register Infrastructure Services
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<IDpiService, DpiService>();
            services.AddSingleton<IImageProcessor, ImageProcessor>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IScheduler>(provider => new Scheduler(Application.Current.Dispatcher));

            // Register Repositories
            services.AddSingleton<IProfileRepository, ProfileRepository>();
            services.AddSingleton<IDataRecordRepository, DataRecordRepository>();

            // Register Core Services - Order matters here!
            // Register fast-initializing services first
            services.AddSingleton<ISettingsManager, SettingsManager>();
            services.AddSingleton<IDataManager, DataManager>();
            services.AddSingleton<IGameProfileManager, GameProfileManager>();
            services.AddSingleton<IScreenCapture, ScreenCapture>();
            services.AddSingleton<IHotKeyManager, HotKeyManager>();

            // Register AI Services
            services.AddSingleton<INvidiaOCRService, NvidiaOCRService>();
            services.AddSingleton<IAIQueueProcessor, AIQueueProcessor>();

            // Register slow-initializing OCR-related services last with error handling
            services.AddSingleton<IOCRProcessor>(provider =>
            {
                try
                {
                    Log.Information("Initializing OCR Processor...");
                    return new OCRProcessor(
                        provider.GetRequiredService<IImageProcessor>(),
                        provider.GetRequiredService<IFileSystem>(),
                        provider.GetRequiredService<ISettingsManager>()
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize OCR processor");
                    throw;
                }
            });
            services.AddSingleton<IScanner>(provider =>
            {
                try
                {
                    Log.Information("Initializing Scanner...");
                    return new Scanner(
                        provider.GetRequiredService<IDataManager>(),
                        provider.GetRequiredService<IOCRProcessor>(),
                        provider.GetRequiredService<IScreenCapture>(),
                        provider.GetRequiredService<IAIQueueProcessor>(),
                        provider.GetRequiredService<ISettingsManager>()
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize Scanner");
                    throw;
                }
            });

            Log.Information("Service Locator successfully configured all services");

            _serviceProvider = services.BuildServiceProvider();

            // Note: OCRProcessor.Initialize() will be called from App.xaml.cs after UI shows
            // This allows the UI to appear before Tesseract engines are initialized
            Log.Information("Service provider built successfully - OCR will initialize after UI shows");
        }

        public static T GetService<T>() where T : class
        {
            if (_serviceProvider == null)
            {
                throw new InvalidOperationException(
                    "Service provider is not initialized. Call Initialize() before requesting any services.");
            }

            var service = _serviceProvider.GetService<T>();
            if (service == null)
            {
                throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
            }

            return service;
        }
    }
}
