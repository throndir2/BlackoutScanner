using System.Drawing;

namespace BlackoutScanner
{
    public static class Extensions
    {
        // Extension method to offset rectangles by the game window's location
        public static Rectangle OffsetBy(this Rectangle rect, Point offset)
        {
            return new Rectangle(rect.Left + offset.X, rect.Top + offset.Y, rect.Width, rect.Height);
        }
    }

}
