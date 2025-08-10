using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BlackoutScanner.Interfaces;
using Serilog;

namespace BlackoutScanner.Services
{
    public class HotKeyManager : IHotKeyManager, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private int _currentId = 9000;
        private Action? _hotkeyAction;
        private int _registeredId = -1;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public void Initialize(Window window)
        {
            if (window == null)
            {
                Log.Error("Cannot initialize HotKeyManager: window is null");
                return;
            }
            
            var helper = new WindowInteropHelper(window);
            
            // Ensure we have a handle
            if (helper.Handle == IntPtr.Zero)
            {
                Log.Debug("Window handle not available, ensuring handle creation");
                helper.EnsureHandle();
            }
            
            _windowHandle = helper.Handle;
            
            if (_windowHandle == IntPtr.Zero)
            {
                Log.Error("Failed to get window handle even after EnsureHandle");
                return;
            }
            
            _source = HwndSource.FromHwnd(_windowHandle);
            
            if (_source != null)
            {
                _source.AddHook(HwndHook);
                Log.Debug($"HotKeyManager initialized successfully with window handle: {_windowHandle}");
            }
            else
            {
                Log.Error("Failed to create HwndSource from window handle");
            }
        }

        public bool RegisterHotKey(string hotKeyString, Action action)
        {
            UnregisterCurrentHotKey();
            
            if (!ParseHotKey(hotKeyString, out uint modifiers, out uint key))
            {
                Log.Error($"Failed to parse hotkey string: {hotKeyString}");
                return false;
            }

            _registeredId = _currentId++;
            _hotkeyAction = action;
            
            bool result = RegisterHotKey(_windowHandle, _registeredId, modifiers, key);
            
            if (result)
            {
                Log.Information($"Successfully registered hotkey: {hotKeyString} (ID: {_registeredId}, Modifiers: {modifiers}, Key: {key})");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                Log.Error($"Failed to register hotkey: {hotKeyString}. Win32 Error: {error}");
            }
            
            return result;
        }

        private void UnregisterCurrentHotKey()
        {
            if (_registeredId != -1 && _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, _registeredId);
                _registeredId = -1;
            }
        }

        private bool ParseHotKey(string hotKeyString, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;

            if (string.IsNullOrWhiteSpace(hotKeyString))
                return false;

            var parts = hotKeyString.Split('+');
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                    modifiers |= 0x0002; // MOD_CONTROL
                else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= 0x0001; // MOD_ALT
                else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= 0x0004; // MOD_SHIFT
                else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                    modifiers |= 0x0008; // MOD_WIN
                else
                {
                    // Try to parse the key
                    if (Enum.TryParse<Key>(trimmed, true, out var wpfKey))
                    {
                        key = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return key != 0;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var receivedId = wParam.ToInt32();
                if (receivedId == _registeredId)
                {
                    Log.Debug($"Hotkey triggered: ID {receivedId}");
                    _hotkeyAction?.Invoke();
                    handled = true;
                }
                else
                {
                    Log.Debug($"Received hotkey message with ID {receivedId}, but we're looking for {_registeredId}");
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterCurrentHotKey();
            _source?.RemoveHook(HwndHook);
            _source?.Dispose();
        }
    }
}
