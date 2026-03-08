using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yuukei.Runtime
{
    public sealed class WindowsDesktopAdapter : IDesktopPlatformAdapter
    {
        private readonly Dictionary<ShortcutAction, ShortcutBinding> _shortcuts = new Dictionary<ShortcutAction, ShortcutBinding>();
        private readonly Dictionary<ShortcutAction, bool> _shortcutLatch = new Dictionary<ShortcutAction, bool>();
        private readonly string _secretDirectory = Path.Combine(Application.persistentDataPath, "secure");

        public event Action<TrayCommand> TrayCommandRequested;
        public event Action<ShortcutAction> ShortcutTriggered;

        public void Initialize()
        {
            Directory.CreateDirectory(_secretDirectory);
        }

        public void Shutdown()
        {
        }

        public void Tick()
        {
            if (!Application.isFocused || Keyboard.current == null)
            {
                ResetShortcutLatch();
                return;
            }

            foreach (var pair in _shortcuts)
            {
                var active = pair.Value.IsPressed();
                if (active && (!_shortcutLatch.TryGetValue(pair.Key, out var latched) || !latched))
                {
                    _shortcutLatch[pair.Key] = true;
                    ShortcutTriggered?.Invoke(pair.Key);
                }
                else if (!active)
                {
                    _shortcutLatch[pair.Key] = false;
                }
            }
        }

        public void ApplyShortcuts(ShortcutConfigData shortcutConfig)
        {
            _shortcuts.Clear();
            _shortcutLatch.Clear();

            RegisterShortcut(ShortcutAction.ToggleDisabled, shortcutConfig?.ToggleDisabled);
            RegisterShortcut(ShortcutAction.ToggleHidden, shortcutConfig?.ToggleHidden);
            RegisterShortcut(ShortcutAction.OpenSettings, shortcutConfig?.OpenSettings);
        }

        public RectInt GetVirtualDesktopBounds()
        {
            var displays = GetDisplays();
            if (displays.Count == 0)
            {
                return new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            foreach (var display in displays)
            {
                minX = Mathf.Min(minX, display.Bounds.xMin);
                minY = Mathf.Min(minY, display.Bounds.yMin);
                maxX = Mathf.Max(maxX, display.Bounds.xMax);
                maxY = Mathf.Max(maxY, display.Bounds.yMax);
            }

            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        public IReadOnlyList<DesktopDisplayInfo> GetDisplays()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var list = new List<DesktopDisplayInfo>();
            var index = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx();
                info.cbSize = Marshal.SizeOf<MonitorInfoEx>();
                if (GetMonitorInfo(monitor, ref info))
                {
                    var bounds = RectIntFromNative(info.rcMonitor);
                    list.Add(new DesktopDisplayInfo(index, bounds));
                    index++;
                }

                return true;
            }, IntPtr.Zero);

            if (list.Count > 0)
            {
                return list;
            }
#endif
            return new[]
            {
                new DesktopDisplayInfo(0, new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height)),
            };
        }

        public int GetForegroundDisplayIndex()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return -1;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return -1;
            }

            var monitorInfo = new MonitorInfoEx();
            monitorInfo.cbSize = Marshal.SizeOf<MonitorInfoEx>();
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return -1;
            }

            var bounds = RectIntFromNative(monitorInfo.rcMonitor);
            var displays = GetDisplays();
            for (var i = 0; i < displays.Count; i++)
            {
                if (displays[i].Bounds == bounds)
                {
                    return displays[i].Index;
                }
            }
#endif
            return 0;
        }

        public bool IsForegroundWindowFullscreen()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || !GetWindowRect(window, out var windowRect))
            {
                return false;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MonitorInfoEx();
            monitorInfo.cbSize = Marshal.SizeOf<MonitorInfoEx>();
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            return RectIntFromNative(windowRect) == RectIntFromNative(monitorInfo.rcMonitor);
#else
            return false;
#endif
        }

        public float GetGlobalIdleSeconds()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var inputInfo = new LastInputInfo
            {
                cbSize = (uint)Marshal.SizeOf<LastInputInfo>(),
            };

            if (!GetLastInputInfo(ref inputInfo))
            {
                return 0f;
            }

            var elapsed = unchecked((uint)Environment.TickCount - inputInfo.dwTime);
            return elapsed / 1000f;
#else
            return 0f;
#endif
        }

        public bool TryLoadSecret(string key, out string value)
        {
            value = string.Empty;
            var path = GetSecretPath(key);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var encrypted = File.ReadAllBytes(path);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                var bytes = UnprotectBytes(encrypted);
#else
                var bytes = encrypted;
#endif
                value = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WindowsDesktopAdapter] Failed to load secret '{key}': {exception.Message}");
                return false;
            }
        }

        public void SaveSecret(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(_secretDirectory);
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                bytes = ProtectBytes(bytes);
#endif
                File.WriteAllBytes(GetSecretPath(key), bytes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WindowsDesktopAdapter] Failed to save secret '{key}': {exception.Message}");
            }
        }

        public void DeleteSecret(string key)
        {
            var path = GetSecretPath(key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void OpenUrl(string url)
        {
            Application.OpenURL(url);
        }

        private void RegisterShortcut(ShortcutAction action, string shortcutText)
        {
            if (ShortcutBinding.TryParse(shortcutText, out var binding))
            {
                _shortcuts[action] = binding;
                _shortcutLatch[action] = false;
            }
        }

        private void ResetShortcutLatch()
        {
            foreach (var action in new List<ShortcutAction>(_shortcutLatch.Keys))
            {
                _shortcutLatch[action] = false;
            }
        }

        private string GetSecretPath(string key)
        {
            var safeName = key.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            return Path.Combine(_secretDirectory, safeName + ".bin");
        }

        private static RectInt RectIntFromNative(NativeRect nativeRect)
        {
            return new RectInt(nativeRect.Left, nativeRect.Top, nativeRect.Right - nativeRect.Left, nativeRect.Bottom - nativeRect.Top);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static byte[] ProtectBytes(byte[] input)
        {
            var inputBlob = CreateDataBlob(input);
            try
            {
                if (!CryptProtectData(ref inputBlob, "YuukeiSecret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outputBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return CopyAndFreeDataBlob(ref outputBlob);
            }
            finally
            {
                FreeDataBlob(ref inputBlob);
            }
        }

        private static byte[] UnprotectBytes(byte[] input)
        {
            var inputBlob = CreateDataBlob(input);
            try
            {
                if (!CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outputBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return CopyAndFreeDataBlob(ref outputBlob);
            }
            finally
            {
                FreeDataBlob(ref inputBlob);
            }
        }

        private static DataBlob CreateDataBlob(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return new DataBlob();
            }

            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return new DataBlob
            {
                cbData = bytes.Length,
                pbData = pointer,
            };
        }

        private static void FreeDataBlob(ref DataBlob blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob.pbData);
                blob.pbData = IntPtr.Zero;
            }

            blob.cbData = 0;
        }

        private static byte[] CopyAndFreeDataBlob(ref DataBlob blob)
        {
            try
            {
                if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
                {
                    return Array.Empty<byte>();
                }

                var bytes = new byte[blob.cbData];
                Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
                return bytes;
            }
            finally
            {
                if (blob.pbData != IntPtr.Zero)
                {
                    LocalFree(blob.pbData);
                    blob.pbData = IntPtr.Zero;
                }

                blob.cbData = 0;
            }
        }
#endif

        private readonly struct ShortcutBinding
        {
            public ShortcutBinding(bool ctrl, bool shift, bool alt, Key key)
            {
                Ctrl = ctrl;
                Shift = shift;
                Alt = alt;
                Key = key;
            }

            public bool Ctrl { get; }
            public bool Shift { get; }
            public bool Alt { get; }
            public Key Key { get; }

            public bool IsPressed()
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                {
                    return false;
                }

                if (Ctrl && !(keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
                {
                    return false;
                }

                if (Shift && !(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
                {
                    return false;
                }

                if (Alt && !(keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed))
                {
                    return false;
                }

                var keyControl = keyboard[Key];
                return keyControl != null && keyControl.isPressed;
            }

            public static bool TryParse(string text, out ShortcutBinding binding)
            {
                binding = default;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var ctrl = false;
                var shift = false;
                var alt = false;
                Key? key = null;

                foreach (var rawPart in text.Split('+'))
                {
                    var part = rawPart.Trim();
                    if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    {
                        ctrl = true;
                        continue;
                    }

                    if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    {
                        shift = true;
                        continue;
                    }

                    if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    {
                        alt = true;
                        continue;
                    }

                    key = ParseKey(part);
                }

                if (!key.HasValue)
                {
                    return false;
                }

                binding = new ShortcutBinding(ctrl, shift, alt, key.Value);
                return true;
            }

            private static Key? ParseKey(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (text == ";")
                {
                    return Key.Semicolon;
                }

                if (text.Length == 1 && char.IsLetter(text[0]))
                {
                    return Enum.TryParse<Key>(text.ToUpperInvariant(), out var parsed) ? parsed : null;
                }

                if (Enum.TryParse<Key>(text, true, out var key))
                {
                    return key;
                }

                return null;
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int CryptProtectUiForbidden = 0x1;
        private const uint MonitorDefaultToNearest = 2;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo plii);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
#endif
    }
}
