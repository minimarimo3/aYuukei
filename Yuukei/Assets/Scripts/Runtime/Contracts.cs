using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Daihon;
using UnityEngine;

namespace Yuukei.Runtime
{
    public enum ShortcutAction
    {
        OpenSettings,
        ToggleDisabled,
        ToggleHidden,
    }

    public enum TrayCommand
    {
        OpenSettings,
        ToggleDisabled,
        ToggleHidden,
        Exit,
    }

    public readonly struct DesktopDisplayInfo
    {
        public DesktopDisplayInfo(int index, RectInt bounds)
        {
            Index = index;
            Bounds = bounds;
        }

        public int Index { get; }
        public RectInt Bounds { get; }
    }

    public readonly struct AppShellState
    {
        public AppShellState(bool isSettingsVisible, bool isTemporarilyDisabled, bool isTemporarilyHidden)
        {
            IsSettingsVisible = isSettingsVisible;
            IsTemporarilyDisabled = isTemporarilyDisabled;
            IsTemporarilyHidden = isTemporarilyHidden;
        }

        public bool IsSettingsVisible { get; }
        public bool IsTemporarilyDisabled { get; }
        public bool IsTemporarilyHidden { get; }
    }

    public readonly struct ShortcutRegistrationStatus
    {
        public ShortcutRegistrationStatus(string bindingText, bool isRegistered, string message)
        {
            BindingText = bindingText ?? string.Empty;
            IsRegistered = isRegistered;
            Message = message ?? string.Empty;
        }

        public string BindingText { get; }
        public bool IsRegistered { get; }
        public string Message { get; }
    }

    public interface IDesktopPlatformAdapter
    {
        event Action<TrayCommand> TrayCommandRequested;
        event Action<ShortcutAction> ShortcutTriggered;

        void Initialize();
        void Shutdown();
        void Tick();
        void ApplyShortcuts(ShortcutConfigData shortcutConfig);
        void UpdateShellState(AppShellState state);
        IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> GetShortcutStatuses();
        RectInt GetVirtualDesktopBounds();
        IReadOnlyList<DesktopDisplayInfo> GetDisplays();
        int GetForegroundDisplayIndex();
        bool IsForegroundWindowFullscreen();
        float GetGlobalIdleSeconds();
        bool TryLoadSecret(string key, out string value);
        void SaveSecret(string key, string value);
        void DeleteSecret(string key);
        void OpenUrl(string url);
    }

    public interface IPackageContentResolver
    {
        PackageContentSelection ResolveActiveContent(ResolvedPackage package, OverrideSelections overrides);
    }

    public delegate UniTask<DaihonValue?> CanonicalFunctionDelegate(DaihonValue[] args, CancellationToken cancellationToken);
}
