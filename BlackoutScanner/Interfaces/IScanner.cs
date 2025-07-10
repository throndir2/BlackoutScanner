using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using BlackoutScanner.Models;

namespace BlackoutScanner.Interfaces
{
    public interface IScanner
    {
        event Action<Dictionary<string, object>>? DataUpdated;
        event Action<string, BitmapImage>? ImageUpdated;
        event Action<DateTime>? ScanDateUpdated;
        event Action<string>? CategoryScanning;

        void StartScanning(GameProfile profile);
        void StopScanning();
        void UpdateDebugSettings(bool saveDebugImages, bool verboseLogging, string debugImagesFolder);
    }
}
