using System;
using System.Collections.Generic;
using System.Drawing;

namespace BlackoutScanner.Interfaces
{
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
    }

    public interface IWindowManager
    {
        IEnumerable<WindowInfo> GetAllWindows();
        IntPtr FindWindowByTitle(string title);
        bool SetForegroundWindow(IntPtr handle);
        Rectangle GetWindowRect(IntPtr handle);
        Rectangle GetClientRect(IntPtr handle);
    }
}
