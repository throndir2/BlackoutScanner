using System.Drawing;

namespace BlackoutScanner.Models
{
    public class RelativeBounds
    {
        // Store as percentages (0.0 to 1.0)
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public RelativeBounds() { }

        public RelativeBounds(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        // Convert from absolute rectangle to relative bounds
        public static RelativeBounds FromAbsolute(Rectangle absolute, Rectangle container)
        {
            return new RelativeBounds
            {
                X = (double)absolute.X / container.Width,
                Y = (double)absolute.Y / container.Height,
                Width = (double)absolute.Width / container.Width,
                Height = (double)absolute.Height / container.Height
            };
        }

        // Convert from relative bounds to absolute rectangle
        public Rectangle ToAbsolute(Rectangle container)
        {
            return new Rectangle(
                (int)(X * container.Width),
                (int)(Y * container.Height),
                (int)(Width * container.Width),
                (int)(Height * container.Height)
            );
        }

        public override string ToString()
        {
            return $"X: {X:P1}, Y: {Y:P1}, W: {Width:P1}, H: {Height:P1}";
        }
    }
}
