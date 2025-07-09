using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BlackoutScanner.Utilities
{
    public static class DpiHelper
    {
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness_Internal(PROCESS_DPI_AWARENESS value);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hProcess, out PROCESS_DPI_AWARENESS value);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint DEFAULT_DPI = 96;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public enum PROCESS_DPI_AWARENESS
        {
            Process_DPI_Unaware = 0,
            Process_System_DPI_Aware = 1,
            Process_Per_Monitor_DPI_Aware = 2
        }

        private enum MonitorDpiType
        {
            EffectiveDpi = 0,
            AngularDpi = 1,
            RawDpi = 2
        }

        public static PROCESS_DPI_AWARENESS SetProcessDpiAwareness(PROCESS_DPI_AWARENESS awareness)
        {
            SetProcessDpiAwareness_Internal(awareness);
            GetProcessDpiAwareness(IntPtr.Zero, out var currentAwareness);
            return currentAwareness;
        }

        public static (double dpiX, double dpiY) GetDpiForWindow(IntPtr hwnd)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out uint dpiX, out uint dpiY);
            return (dpiX, dpiY);
        }

        public static (double dpiX, double dpiY) GetDpiForPoint(System.Drawing.Point point)
        {
            var pt = new POINT { X = point.X, Y = point.Y };
            var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out uint dpiX, out uint dpiY);
            return (dpiX, dpiY);
        }

        public static double GetScaleFactorForWindow(IntPtr hwnd)
        {
            var (dpiX, _) = GetDpiForWindow(hwnd);
            return dpiX / 96.0; // 96 DPI is the standard
        }

        public static System.Drawing.Rectangle ConvertToPhysicalPixels(System.Drawing.Rectangle logicalRect, IntPtr hwnd)
        {
            var scaleFactor = GetScaleFactorForWindow(hwnd);
            return new System.Drawing.Rectangle(
                (int)(logicalRect.X * scaleFactor),
                (int)(logicalRect.Y * scaleFactor),
                (int)(logicalRect.Width * scaleFactor),
                (int)(logicalRect.Height * scaleFactor)
            );
        }

        public static System.Drawing.Rectangle ConvertToLogicalPixels(System.Drawing.Rectangle physicalRect, IntPtr hwnd)
        {
            var scaleFactor = GetScaleFactorForWindow(hwnd);
            return new System.Drawing.Rectangle(
                (int)(physicalRect.X / scaleFactor),
                (int)(physicalRect.Y / scaleFactor),
                (int)(physicalRect.Width / scaleFactor),
                (int)(physicalRect.Height / scaleFactor)
            );
        }

        public static double GetDpiScaleFactor(Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }

        public static Point LogicalToPhysicalPoint(Visual visual, Point logicalPoint)
        {
            double dpiScale = GetDpiScaleFactor(visual);
            return new Point(logicalPoint.X * dpiScale, logicalPoint.Y * dpiScale);
        }

        public static Point PhysicalToLogicalPoint(Visual visual, Point physicalPoint)
        {
            double dpiScale = GetDpiScaleFactor(visual);
            return new Point(physicalPoint.X / dpiScale, physicalPoint.Y / dpiScale);
        }
    }
}
