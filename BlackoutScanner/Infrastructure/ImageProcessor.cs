using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using BlackoutScanner.Interfaces;

namespace BlackoutScanner.Infrastructure
{
    public class ImageProcessor : IImageProcessor
    {
        public BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Important for cross-thread access

                return bitmapImage;
            }
        }

        public Bitmap CropBitmap(Bitmap source, Rectangle cropArea)
        {
            if (cropArea.Width <= 0 || cropArea.Height <= 0)
                return new Bitmap(1, 1);

            Bitmap target = new Bitmap(cropArea.Width, cropArea.Height);

            using (Graphics g = Graphics.FromImage(target))
            {
                g.DrawImage(source,
                    new Rectangle(0, 0, target.Width, target.Height),
                    cropArea,
                    GraphicsUnit.Pixel);
            }

            return target;
        }

        public string ComputeImageHash(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);

                using (var sha = MD5.Create())
                {
                    byte[] hash = sha.ComputeHash(ms);
                    return BitConverter.ToString(hash).Replace("-", String.Empty).ToLower();
                }
            }
        }

        public void SaveBitmap(Bitmap bitmap, string filePath)
        {
            // Ensure directory exists
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bitmap.Save(filePath, ImageFormat.Png);
        }
    }
}
