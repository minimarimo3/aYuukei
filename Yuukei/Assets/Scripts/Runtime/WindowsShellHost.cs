using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yuukei.Runtime
{
    internal readonly struct ShortcutBinding
    {
        public ShortcutBinding(string originalText, bool ctrl, bool shift, bool alt, Key key)
        {
            OriginalText = originalText ?? string.Empty;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
            Key = key;
        }

        public string OriginalText { get; }
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

        public bool TryGetWindowsHotKey(out WindowsHotKey hotKey)
        {
            hotKey = default;
            if (!TryGetVirtualKey(Key, out var virtualKey))
            {
                return false;
            }

            var modifiers = WindowsNativeShellHost.ModNoRepeat;
            if (Ctrl)
            {
                modifiers |= WindowsNativeShellHost.ModControl;
            }

            if (Shift)
            {
                modifiers |= WindowsNativeShellHost.ModShift;
            }

            if (Alt)
            {
                modifiers |= WindowsNativeShellHost.ModAlt;
            }

            hotKey = new WindowsHotKey(modifiers, virtualKey);
            return true;
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

                if (!TryParseKey(part, out var parsedKey))
                {
                    return false;
                }

                key = parsedKey;
            }

            if (!key.HasValue)
            {
                return false;
            }

            binding = new ShortcutBinding(text, ctrl, shift, alt, key.Value);
            return true;
        }

        private static bool TryParseKey(string text, out Key key)
        {
            key = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (text == ";")
            {
                key = Key.Semicolon;
                return true;
            }

            if (text.Length == 1 && char.IsLetter(text[0]))
            {
                return Enum.TryParse(text.ToUpperInvariant(), out key);
            }

            if (text.Length == 1 && char.IsDigit(text[0]))
            {
                return text[0] switch
                {
                    '0' => TryAssign(Key.Digit0, out key),
                    '1' => TryAssign(Key.Digit1, out key),
                    '2' => TryAssign(Key.Digit2, out key),
                    '3' => TryAssign(Key.Digit3, out key),
                    '4' => TryAssign(Key.Digit4, out key),
                    '5' => TryAssign(Key.Digit5, out key),
                    '6' => TryAssign(Key.Digit6, out key),
                    '7' => TryAssign(Key.Digit7, out key),
                    '8' => TryAssign(Key.Digit8, out key),
                    '9' => TryAssign(Key.Digit9, out key),
                    _ => false,
                };
            }

            return Enum.TryParse(text, true, out key);
        }

        private static bool TryGetVirtualKey(Key key, out uint virtualKey)
        {
            virtualKey = 0;
            if (key >= Key.A && key <= Key.Z)
            {
                virtualKey = (uint)('A' + (key - Key.A));
                return true;
            }

            switch (key)
            {
                case Key.Digit0:
                    virtualKey = 0x30;
                    return true;
                case Key.Digit1:
                    virtualKey = 0x31;
                    return true;
                case Key.Digit2:
                    virtualKey = 0x32;
                    return true;
                case Key.Digit3:
                    virtualKey = 0x33;
                    return true;
                case Key.Digit4:
                    virtualKey = 0x34;
                    return true;
                case Key.Digit5:
                    virtualKey = 0x35;
                    return true;
                case Key.Digit6:
                    virtualKey = 0x36;
                    return true;
                case Key.Digit7:
                    virtualKey = 0x37;
                    return true;
                case Key.Digit8:
                    virtualKey = 0x38;
                    return true;
                case Key.Digit9:
                    virtualKey = 0x39;
                    return true;
                case Key.Semicolon:
                    virtualKey = 0xBA;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryAssign(Key value, out Key key)
        {
            key = value;
            return true;
        }
    }

    internal readonly struct WindowsHotKey
    {
        public WindowsHotKey(uint modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        public uint Modifiers { get; }
        public uint VirtualKey { get; }
    }

    internal interface IWindowsShellHost
    {
        event Action<TrayCommand> TrayCommandRequested;
        event Action<ShortcutAction> ShortcutTriggered;

        bool SupportsGlobalHotkeys { get; }

        void Initialize();
        void Shutdown();
        void Tick();
        void ApplyShellState(AppShellState state);
        IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> ApplyShortcuts(IReadOnlyDictionary<ShortcutAction, ShortcutBinding> shortcuts);
    }

    internal sealed class WindowsNativeShellHost : IWindowsShellHost
    {
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModShift = 0x0004;
        internal const uint ModNoRepeat = 0x4000;

        private const uint WmApp = 0x8000;
        private const uint WmCommand = 0x0111;
        private const uint WmContextMenu = 0x007B;
        private const uint WmDestroy = 0x0002;
        private const uint WmHotKey = 0x0312;
        private const uint WmNcCreate = 0x0081;
        private const uint WmNcDestroy = 0x0082;
        private const uint WmNull = 0x0000;
        private const uint WmRButtonUp = 0x0205;
        private const uint WmLButtonUp = 0x0202;

        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint NimAdd = 0x00000000;
        private const uint NimModify = 0x00000001;
        private const uint NimDelete = 0x00000002;

        private const uint MfString = 0x00000000;
        private const uint MfSeparator = 0x00000800;
        private const uint MfChecked = 0x00000008;
        private const uint TpmLeftAlign = 0x0000;
        private const uint TpmBottomAlign = 0x0020;
        private const uint TpmRightButton = 0x0002;
        private const uint PmRemove = 0x0001;

        private const int IdApplication = 32512;
        private const int MenuIdSettings = 1001;
        private const int MenuIdToggleDisabled = 1002;
        private const int MenuIdToggleHidden = 1003;
        private const int MenuIdExit = 1004;
        private const int NotifyIconId = 1;
        private const uint TrayMessageId = WmApp + 1;

        private static readonly Dictionary<IntPtr, WindowsNativeShellHost> Hosts = new Dictionary<IntPtr, WindowsNativeShellHost>();
        private static readonly WndProc WindowProcedure = StaticWindowProcedure;

        private readonly Dictionary<int, ShortcutAction> _hotkeyActions = new Dictionary<int, ShortcutAction>();
        private readonly Dictionary<ShortcutAction, int> _registeredHotkeyIds = new Dictionary<ShortcutAction, int>();
        private readonly Dictionary<int, TrayCommand> _menuCommands = new Dictionary<int, TrayCommand>
        {
            [MenuIdSettings] = TrayCommand.OpenSettings,
            [MenuIdToggleDisabled] = TrayCommand.ToggleDisabled,
            [MenuIdToggleHidden] = TrayCommand.ToggleHidden,
            [MenuIdExit] = TrayCommand.Exit,
        };

        private GCHandle _selfHandle;
        private IntPtr _windowHandle;
        private IntPtr _iconHandle;
        private bool _notifyIconAdded;
        private bool _initialized;
        private AppShellState _shellState;

        public event Action<TrayCommand> TrayCommandRequested;
        public event Action<ShortcutAction> ShortcutTriggered;

        public bool SupportsGlobalHotkeys => true;

        public void Initialize()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_initialized)
            {
                return;
            }

            _selfHandle = GCHandle.Alloc(this);
            var className = "Yuukei.WindowsShellHost";
            var instance = GetModuleHandle(null);
            var windowClass = new WindowClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
                lpfnWndProc = WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };

            var atom = RegisterClassEx(ref windowClass);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1410)
                {
                    throw new Win32Exception(error);
                }
            }

            _windowHandle = CreateWindowEx(
                0,
                className,
                "Yuukei Shell Host",
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                GCHandle.ToIntPtr(_selfHandle));

            if (_windowHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                CleanupHandle();
                throw new Win32Exception(error);
            }

            _iconHandle = LoadIcon(IntPtr.Zero, (IntPtr)IdApplication);
            UpdateNotifyIcon(NimAdd);
            _initialized = true;
#endif
        }

        public void Shutdown()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            UnregisterAllHotkeys();
            RemoveNotifyIcon();

            if (_windowHandle != IntPtr.Zero)
            {
                DestroyWindow(_windowHandle);
                Hosts.Remove(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }

            CleanupHandle();
            _initialized = false;
#endif
        }

        public void Tick()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            while (PeekMessage(out var message, _windowHandle, 0, 0, PmRemove))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
#endif
        }

        public void ApplyShellState(AppShellState state)
        {
            _shellState = state;
            if (_notifyIconAdded)
            {
                UpdateNotifyIcon(NimModify);
            }
        }

        public IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> ApplyShortcuts(IReadOnlyDictionary<ShortcutAction, ShortcutBinding> shortcuts)
        {
            var statuses = new Dictionary<ShortcutAction, ShortcutRegistrationStatus>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            UnregisterAllHotkeys();

            foreach (var pair in shortcuts)
            {
                if (!pair.Value.TryGetWindowsHotKey(out var hotKey))
                {
                    statuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, false, "Windows で使えないキーです。");
                    continue;
                }

                var hotKeyId = 0x2000 + (int)pair.Key;
                if (RegisterHotKey(_windowHandle, hotKeyId, hotKey.Modifiers, hotKey.VirtualKey))
                {
                    _registeredHotkeyIds[pair.Key] = hotKeyId;
                    _hotkeyActions[hotKeyId] = pair.Key;
                    statuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, true, "グローバルホットキーとして登録済み");
                    continue;
                }

                var error = Marshal.GetLastWin32Error();
                statuses[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, false, $"RegisterHotKey 失敗 ({error})");
            }
#endif
            return statuses;
        }

        private void UnregisterAllHotkeys()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_windowHandle == IntPtr.Zero)
            {
                _registeredHotkeyIds.Clear();
                _hotkeyActions.Clear();
                return;
            }

            foreach (var hotKeyId in _registeredHotkeyIds.Values)
            {
                UnregisterHotKey(_windowHandle, hotKeyId);
            }

            _registeredHotkeyIds.Clear();
            _hotkeyActions.Clear();
#endif
        }

        private void UpdateNotifyIcon(uint message)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var notifyIcon = CreateNotifyIconData();
            if (!Shell_NotifyIcon(message, ref notifyIcon))
            {
                var error = Marshal.GetLastWin32Error();
                if (message == NimAdd)
                {
                    throw new Win32Exception(error);
                }

                Debug.LogWarning($"[WindowsNativeShellHost] Failed to update tray icon ({error}).");
                return;
            }

            _notifyIconAdded = message != NimDelete;
#endif
        }

        private void RemoveNotifyIcon()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!_notifyIconAdded)
            {
                return;
            }

            var notifyIcon = CreateNotifyIconData();
            Shell_NotifyIcon(NimDelete, ref notifyIcon);
            _notifyIconAdded = false;
#endif
        }

        private NotifyIconData CreateNotifyIconData()
        {
            return new NotifyIconData
            {
                cbSize = Marshal.SizeOf<NotifyIconData>(),
                hWnd = _windowHandle,
                uID = NotifyIconId,
                uFlags = NifMessage | NifIcon | NifTip,
                uCallbackMessage = TrayMessageId,
                hIcon = _iconHandle,
                szTip = BuildTrayToolTip(),
            };
        }

        private string BuildTrayToolTip()
        {
            var suffix = _shellState.IsTemporarilyDisabled
                ? "無効化中"
                : _shellState.IsTemporarilyHidden ? "非表示中" : "待機中";
            var text = $"{Application.productName} - {suffix}";
            return text.Length > 120 ? text.Substring(0, 120) : text;
        }

        private IntPtr HandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case WmCommand:
                    if (_menuCommands.TryGetValue(LowWord(wParam), out var command))
                    {
                        TrayCommandRequested?.Invoke(command);
                        return IntPtr.Zero;
                    }

                    break;
                case WmHotKey:
                    if (_hotkeyActions.TryGetValue(wParam.ToInt32(), out var action))
                    {
                        ShortcutTriggered?.Invoke(action);
                        return IntPtr.Zero;
                    }

                    break;
                case TrayMessageId:
                    var trayMessage = (uint)lParam.ToInt64();
                    if (trayMessage == WmContextMenu || trayMessage == WmRButtonUp || trayMessage == WmLButtonUp)
                    {
                        ShowContextMenu();
                        return IntPtr.Zero;
                    }

                    break;
                case WmDestroy:
                    RemoveNotifyIcon();
                    break;
            }

            return DefWindowProc(_windowHandle, message, wParam, lParam);
        }

        private void ShowContextMenu()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                AppendMenu(menu, MfString, MenuIdSettings, "設定");
                AppendMenu(menu, BuildToggleMenuFlags(_shellState.IsTemporarilyDisabled), MenuIdToggleDisabled, _shellState.IsTemporarilyDisabled ? "再有効化" : "一時無効化");
                AppendMenu(menu, BuildToggleMenuFlags(_shellState.IsTemporarilyHidden), MenuIdToggleHidden, _shellState.IsTemporarilyHidden ? "再表示" : "一時非表示");
                AppendMenu(menu, MfSeparator, 0, string.Empty);
                AppendMenu(menu, MfString, MenuIdExit, "終了");

                if (!GetCursorPos(out var point))
                {
                    return;
                }

                SetForegroundWindow(_windowHandle);
                TrackPopupMenuEx(menu, TpmLeftAlign | TpmBottomAlign | TpmRightButton, point.X, point.Y, _windowHandle, IntPtr.Zero);
                PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(menu);
            }
#endif
        }

        private static uint BuildToggleMenuFlags(bool isActive)
        {
            return isActive ? MfString | MfChecked : MfString;
        }

        private void CleanupHandle()
        {
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        private static int LowWord(IntPtr value)
        {
            return unchecked((short)(value.ToInt64() & 0xffff));
        }

        private static IntPtr StaticWindowProcedure(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmNcCreate)
            {
                var createStruct = Marshal.PtrToStructure<CreateStruct>(lParam);
                var gcHandle = GCHandle.FromIntPtr(createStruct.lpCreateParams);
                if (gcHandle.Target is WindowsNativeShellHost host)
                {
                    Hosts[windowHandle] = host;
                    host._windowHandle = windowHandle;
                }
            }

            if (Hosts.TryGetValue(windowHandle, out var target))
            {
                var result = target.HandleWindowMessage(message, wParam, lParam);
                if (message == WmNcDestroy)
                {
                    Hosts.Remove(windowHandle);
                }

                return result;
            }

            return DefWindowProc(windowHandle, message, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr hWnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public Point pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CreateStruct
        {
            public IntPtr lpCreateParams;
            public IntPtr hInstance;
            public IntPtr hMenu;
            public IntPtr hwndParent;
            public int cy;
            public int cx;
            public int y;
            public int x;
            public int style;
            public IntPtr lpszName;
            public IntPtr lpszClass;
            public uint dwExStyle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClassEx
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClassEx lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Message lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);
    }
}
