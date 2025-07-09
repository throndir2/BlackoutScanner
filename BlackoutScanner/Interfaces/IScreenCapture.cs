using System;
using System.Drawing;

namespace BlackoutScanner.Interfaces
{
    public interface IScreenCapture
    {
        Bitmap CaptureScreenArea(Rectangle area);
        Rectangle GetGameWindowRect(string windowTitle);
        Rectangle GetGameWindowRectEx(string windowTitle);
        IntPtr GetGameWindowHandle(string windowTitle);
        Rectangle GetClientRectangle(string windowTitle);
        void BringGameWindowToFront(string windowTitle);
    }
}
