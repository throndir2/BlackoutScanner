using System.Drawing;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Interfaces
{
    public interface IImageProcessor
    {
        BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap);
        Bitmap CropBitmap(Bitmap source, Rectangle cropArea);
        string ComputeImageHash(Bitmap bitmap);
        void SaveBitmap(Bitmap bitmap, string filePath);
    }
}
