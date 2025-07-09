using System;
using System.Drawing;
using System.Runtime.InteropServices;
using BlackoutScanner.Interfaces;

namespace BlackoutScanner.Infrastructure
{
    public class DpiService : IDpiService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        public (double dpiX, double dpiY) GetDpiForWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                IntPtr desktop = IntPtr.Zero;
                IntPtr dc = GetDC(desktop);
                int dpiX = GetDeviceCaps(dc, LOGPIXELSX);
                int dpiY = GetDeviceCaps(dc, LOGPIXELSY);
                ReleaseDC(desktop, dc);
                return (dpiX, dpiY);
            }

            // For Windows 8.1 and above, we can use GetDpiForMonitor
            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                {
                    return ((double)dpiX, (double)dpiY);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Fallback for older Windows versions
            }

            // Fallback method using GetDeviceCaps
            IntPtr hdc = GetDC(hwnd);
            int x = GetDeviceCaps(hdc, LOGPIXELSX);
            int y = GetDeviceCaps(hdc, LOGPIXELSY);
            ReleaseDC(hwnd, hdc);
            return (x, y);
        }

        public (double dpiX, double dpiY) GetDpiForPoint(System.Drawing.Point point)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                {
                    return ((double)dpiX, (double)dpiY);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Fallback for older Windows versions
            }

            // Fallback method
            IntPtr hdc = GetDC(IntPtr.Zero);
            int x = GetDeviceCaps(hdc, LOGPIXELSX);
            int y = GetDeviceCaps(hdc, LOGPIXELSY);
            ReleaseDC(IntPtr.Zero, hdc);
            return (x, y);
        }

        public double GetScaleFactorForWindow(IntPtr hwnd)
        {
            var (dpiX, _) = GetDpiForWindow(hwnd);
            return dpiX / 96.0;
        }

        public Rectangle ConvertToPhysicalPixels(Rectangle logicalRect, IntPtr hwnd)
        {
            double scaleFactor = GetScaleFactorForWindow(hwnd);
            return new Rectangle(
                (int)(logicalRect.X * scaleFactor),
                (int)(logicalRect.Y * scaleFactor),
                (int)(logicalRect.Width * scaleFactor),
                (int)(logicalRect.Height * scaleFactor)
            );
        }
    }
}
