using System;
using System.Drawing;

namespace BlackoutScanner.Interfaces
{
    public interface IDpiService
    {
        (double dpiX, double dpiY) GetDpiForWindow(IntPtr hwnd);
        (double dpiX, double dpiY) GetDpiForPoint(System.Drawing.Point point);
        double GetScaleFactorForWindow(IntPtr hwnd);
        Rectangle ConvertToPhysicalPixels(Rectangle logicalRect, IntPtr hwnd);
    }
}
