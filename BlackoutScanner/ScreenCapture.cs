using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace BlackoutScanner
{
    public class ScreenCapture
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public Bitmap CaptureScreenArea(Rectangle area)
        {
            var bitmap = new Bitmap(area.Width, area.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            }

            return bitmap;
        }

        private IntPtr FindWindowByTitle(string windowTitle)
        {
            IntPtr foundWindow = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        StringBuilder sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();

                        // Try exact match first
                        if (title.Equals(windowTitle, StringComparison.Ordinal))
                        {
                            foundWindow = hWnd;
                            return false; // Stop enumeration
                        }

                        // Try case-insensitive match
                        if (title.Equals(windowTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            foundWindow = hWnd;
                            return false; // Stop enumeration
                        }

                        // Try contains match (for partial titles)
                        if (title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            foundWindow = hWnd;
                            // Don't return false here - keep looking for a better match
                        }
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);

            return foundWindow;
        }

        public Rectangle GetWindowRectangle(string windowTitle)
        {
            IntPtr hWnd = FindWindowByTitle(windowTitle);

            if (hWnd != IntPtr.Zero)
            {
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    // The window rectangle is already in physical pixels
                    var rectangle = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

                    Log.Debug($"Window '{windowTitle}' found at: {rectangle}");
                    return rectangle;
                }
            }

            Log.Warning("Window '{WindowTitle}' not found.", windowTitle);
            return Rectangle.Empty;
        }

        public Rectangle GetClientRectangle(string windowTitle)
        {
            IntPtr hWnd = FindWindowByTitle(windowTitle);
            if (hWnd != IntPtr.Zero)
            {
                if (GetClientRect(hWnd, out RECT clientRect))
                {
                    Point topLeft = new Point(clientRect.Left, clientRect.Top);
                    ClientToScreen(hWnd, ref topLeft);
                    Point bottomRight = new Point(clientRect.Right, clientRect.Bottom);
                    ClientToScreen(hWnd, ref bottomRight);

                    return new Rectangle(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
                }
            }
            return Rectangle.Empty;
        }

        public void BringGameWindowToFront(string windowTitle)
        {
            IntPtr hWnd = FindWindowByTitle(windowTitle);
            if (hWnd != IntPtr.Zero)
            {
                SetForegroundWindow(hWnd);
            }
            else
            {
                Console.WriteLine($"Window with title '{windowTitle}' not found.");
                // Optionally, log this information instead of or in addition to writing to console
            }
        }
    }
}
