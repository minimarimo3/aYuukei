using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// Windows環境向けのデスクトッププラットフォームアダプタ。
    /// トレイアイコン、グローバルホットキー、シークレット保存、ディスプレイ情報取得などを提供する。
    /// </summary>
    public sealed class WindowsDesktopAdapter : IDesktopPlatformAdapter
    {
        private readonly Dictionary<ShortcutAction, ShortcutBinding> _shortcuts = new Dictionary<ShortcutAction, ShortcutBinding>();
        private readonly Dictionary<ShortcutAction, bool> _shortcutLatch = new Dictionary<ShortcutAction, bool>();
        private readonly Dictionary<ShortcutAction, ShortcutRegistrationStatus> _shortcutStatuses = new Dictionary<ShortcutAction, ShortcutRegistrationStatus>();
        private readonly string _secretDirectory = Path.Combine(Application.persistentDataPath, "secure");

        private IWindowsShellHost _shellHost;
        private AppShellState _shellState;

        public WindowsDesktopAdapter()
            : this(null)
        {
        }

        internal WindowsDesktopAdapter(IWindowsShellHost shellHost)
        {
            _shellHost = shellHost;
        }

        public event Action<TrayCommand> TrayCommandRequested;
        public event Action<ShortcutAction> ShortcutTriggered;

        /// <summary>ネイティブシェルホストとシークレット保存ディレクトリを初期化する。</summary>
        public void Initialize()
        {
            Debug.Log("[WindowsDesktopAdapter] 初期化を開始します");
            Directory.CreateDirectory(_secretDirectory);

            if (_shellHost == null)
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                _shellHost = new WindowsNativeShellHost();
#endif
            }

            if (_shellHost == null)
            {
                return;
            }

            try
            {
                _shellHost.TrayCommandRequested += OnShellTrayCommandRequested;
                _shellHost.ShortcutTriggered += OnShellShortcutTriggered;
                _shellHost.Initialize();
                _shellHost.ApplyShellState(_shellState);
                Debug.Log("[WindowsDesktopAdapter] ネイティブシェルホストの初期化に成功しました");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WindowsDesktopAdapter] Native shell host initialization failed. {exception.Message}");
                _shellHost.TrayCommandRequested -= OnShellTrayCommandRequested;
                _shellHost.ShortcutTriggered -= OnShellShortcutTriggered;
                _shellHost = null;
            }
        }

        /// <summary>ネイティブシェルホストをシャットダウンし、リソースを解放する。</summary>
        public void Shutdown()
        {
            Debug.Log("[WindowsDesktopAdapter] シャットダウンを開始します");
            if (_shellHost == null)
            {
                return;
            }

            _shellHost.TrayCommandRequested -= OnShellTrayCommandRequested;
            _shellHost.ShortcutTriggered -= OnShellShortcutTriggered;
            _shellHost.Shutdown();
            _shellHost = null;
        }

        public void Tick()
        {
            _shellHost?.Tick();

            if (_shellHost != null && _shellHost.SupportsGlobalHotkeys)
            {
                return;
            }

            PollForegroundShortcuts();
        }

        /// <summary>ショートカット設定を適用する。グローバルホットキーまたはフォアグラウンドポーリングで動作する。</summary>
        public void ApplyShortcuts(ShortcutConfigData shortcutConfig)
        {
            _shortcuts.Clear();
            _shortcutLatch.Clear();
            _shortcutStatuses.Clear();

            var rawBindings = new Dictionary<ShortcutAction, string>
            {
                [ShortcutAction.ToggleDisabled] = shortcutConfig?.ToggleDisabled ?? string.Empty,
                [ShortcutAction.ToggleHidden] = shortcutConfig?.ToggleHidden ?? string.Empty,
                [ShortcutAction.OpenSettings] = shortcutConfig?.OpenSettings ?? string.Empty,
            };

            var validBindings = new Dictionary<ShortcutAction, ShortcutBinding>();
            foreach (var pair in rawBindings)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    _shortcutStatuses[pair.Key] = new ShortcutRegistrationStatus(string.Empty, false, "未設定");
                    continue;
                }

                if (!ShortcutBinding.TryParse(pair.Value, out var binding))
                {
                    _shortcutStatuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value, false, "無効なショートカットです");
                    continue;
                }

                _shortcuts[pair.Key] = binding;
                _shortcutLatch[pair.Key] = false;
                validBindings[pair.Key] = binding;
            }

            if (_shellHost != null && _shellHost.SupportsGlobalHotkeys)
            {
                var nativeStatuses = _shellHost.ApplyShortcuts(validBindings);
                foreach (var pair in validBindings)
                {
                    if (nativeStatuses.TryGetValue(pair.Key, out var status))
                    {
                        _shortcutStatuses[pair.Key] = status;
                    }
                    else
                    {
                        _shortcutStatuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, false, "グローバルホットキーを登録できませんでした");
                    }
                }

                return;
            }

            foreach (var pair in validBindings)
            {
                _shortcutStatuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, true, "エディタではフォーカス中のみ有効");
            }
        }

        public void UpdateShellState(AppShellState state)
        {
            _shellState = state;
            _shellHost?.ApplyShellState(state);
        }

        public IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> GetShortcutStatuses()
        {
            return new Dictionary<ShortcutAction, ShortcutRegistrationStatus>(_shortcutStatuses);
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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

        private void PollForegroundShortcuts()
        {
            if (!Application.isFocused)
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

        private void OnShellTrayCommandRequested(TrayCommand command)
        {
            TrayCommandRequested?.Invoke(command);
        }

        private void OnShellShortcutTriggered(ShortcutAction action)
        {
            ShortcutTriggered?.Invoke(action);
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
