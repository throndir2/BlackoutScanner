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

            // Configure Serilog
            var serilogLogger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/blackoutscanner-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Register Logger
            services.AddSingleton(serilogLogger);

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

            // Register slow-initializing OCR-related services last
            services.AddSingleton<IOCRProcessor, OCRProcessor>();
            services.AddSingleton<IScanner, Scanner>();

            Log.Information("Service Locator successfully configured all services");

            _serviceProvider = services.BuildServiceProvider();
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
