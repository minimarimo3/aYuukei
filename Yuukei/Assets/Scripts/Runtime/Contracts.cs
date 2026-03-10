using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Daihon;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>ショートカットキーで実行できるアクションの種別。</summary>
    public enum ShortcutAction
    {
        OpenSettings,
        ToggleDisabled,
        ToggleHidden,
    }

    /// <summary>トレイアイコンメニューから送信されるコマンドの種別。</summary>
    public enum TrayCommand
    {
        OpenSettings,
        ToggleDisabled,
        ToggleHidden,
        Exit,
    }

    /// <summary>ディスプレイの情報(インデックスと矩形範囲)。</summary>
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

    /// <summary>アプリケーションのシェル状態(設定画面表示・無効化・非表示)。</summary>
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

    /// <summary>ショートカットキーの登録状態(成功・失敗・メッセージ)。</summary>
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

    /// <summary>デスクトップ OS 固有機能(トレイ、ショートカット、ディスプレイ情報等)への抽象インターフェース。</summary>
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

    /// <summary>パッケージとオーバーライド設定から最終的なコンテンツパスを解決するインターフェース。</summary>
    public interface IPackageContentResolver
    {
        PackageContentSelection ResolveActiveContent(ResolvedPackage package, OverrideSelections overrides);
    }

    /// <summary>台本ランタイムから呼び出される正規関数のデリゲート型。</summary>
    public delegate UniTask<DaihonValue?> CanonicalFunctionDelegate(DaihonValue[] args, CancellationToken cancellationToken);
}
