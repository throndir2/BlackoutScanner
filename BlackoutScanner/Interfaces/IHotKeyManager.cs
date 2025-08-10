using System;
using System.Windows;

namespace BlackoutScanner.Interfaces
{
    public interface IHotKeyManager
    {
        void Initialize(Window window);
        bool RegisterHotKey(string hotKeyString, Action action);
        void Dispose();
    }
}
