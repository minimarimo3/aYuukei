using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Yuukei.Runtime
{
    /// <summary>アプリケーション全体の永続化データ。パッケージ ID、オーバーライド、変数、アプリ状態を保持する。</summary>
    [Serializable]
    public sealed class YuukeiSaveData
    {
        [JsonProperty("activePackageId")]
        public string ActivePackageId = string.Empty;

        [JsonProperty("overrides")]
        public OverrideSelections Overrides = new OverrideSelections();

        [JsonProperty("persistentVariables")]
        public Dictionary<string, object> PersistentVariables = new Dictionary<string, object>();

        [JsonProperty("appState")]
        public AppStateData AppState = new AppStateData();

        public static YuukeiSaveData CreateDefault() => new YuukeiSaveData();
    }

    /// <summary>ユーザーが個別に差し替えた台本・キャラクター・テクスチャ等の選択情報。</summary>
    [Serializable]
    public sealed class OverrideSelections
    {
        [JsonProperty("daihon")]
        public List<string> Daihon = new List<string>();

        [JsonProperty("character")]
        public string Character = string.Empty;

        [JsonProperty("textures")]
        public Dictionary<string, string> Textures = new Dictionary<string, string>();

        [JsonProperty("assets")]
        public Dictionary<string, string> Assets = new Dictionary<string, string>();

        [JsonProperty("motions")]
        public Dictionary<string, string> Motions = new Dictionary<string, string>();

        public void Normalize()
        {
            Daihon ??= new List<string>();
            Textures ??= new Dictionary<string, string>();
            Assets ??= new Dictionary<string, string>();
            Motions ??= new Dictionary<string, string>();
            Daihon.RemoveAll(string.IsNullOrWhiteSpace);
        }

        public void Reset()
        {
            Daihon.Clear();
            Character = string.Empty;
            Textures.Clear();
            Assets.Clear();
            Motions.Clear();
        }
    }

    /// <summary>アプリの一時的な動作状態(無効化・非表示・ショートカット設定)。</summary>
    [Serializable]
    public sealed class AppStateData
    {
        [JsonProperty("isTemporarilyDisabled")]
        public bool IsTemporarilyDisabled;

        [JsonProperty("isTemporarilyHidden")]
        public bool IsTemporarilyHidden;

        [JsonProperty("shortcutConfig")]
        public ShortcutConfigData ShortcutConfig = new ShortcutConfigData();
    }

    /// <summary>ショートカットキーの文字列設定(例: "Ctrl+;")。</summary>
    [Serializable]
    public sealed class ShortcutConfigData
    {
        [JsonProperty("toggleDisabled")]
        public string ToggleDisabled = "Ctrl+;";

        [JsonProperty("toggleHidden")]
        public string ToggleHidden = "Ctrl+Shift+;";

        [JsonProperty("openSettings")]
        public string OpenSettings = "Ctrl+Alt+;";
    }

    /// <summary>パッケージの manifest.json に対応するデータモデル。</summary>
    [Serializable]
    public sealed class PackageManifest
    {
        [JsonProperty("creator")]
        public string Creator = string.Empty;

        [JsonProperty("version")]
        public string Version = string.Empty;

        [JsonProperty("download")]
        public string Download = string.Empty;

        [JsonProperty("license")]
        public string License = string.Empty;

        [JsonProperty("id")]
        public string Id = string.Empty;

        [JsonProperty("character")]
        public string Character = string.Empty;

        [JsonProperty("daihon")]
        public List<string> Daihon = new List<string>();

        [JsonProperty("textures")]
        public Dictionary<string, PackageTextureManifest> Textures = new Dictionary<string, PackageTextureManifest>();

        [JsonProperty("assets")]
        public List<string> Assets = new List<string>();

        [JsonProperty("motions")]
        public List<string> Motions = new List<string>();

        [JsonProperty("dlls")]
        public List<string> Dlls = new List<string>();

        [JsonProperty("aliases")]
        public PackageAliasManifest Aliases = new PackageAliasManifest();

        public void Normalize()
        {
            Daihon ??= new List<string>();
            Textures ??= new Dictionary<string, PackageTextureManifest>();
            Assets ??= new List<string>();
            Motions ??= new List<string>();
            Dlls ??= new List<string>();
            Aliases ??= new PackageAliasManifest();
            Daihon.RemoveAll(string.IsNullOrWhiteSpace);
            Assets.RemoveAll(string.IsNullOrWhiteSpace);
            Motions.RemoveAll(string.IsNullOrWhiteSpace);
            Dlls.RemoveAll(string.IsNullOrWhiteSpace);
        }
    }

    /// <summary>テクスチャの背景・しっぽパスを保持するマニフェスト項目。</summary>
    [Serializable]
    public sealed class PackageTextureManifest
    {
        [JsonProperty("background")]
        public string Background = string.Empty;

        [JsonProperty("tail")]
        public string Tail = string.Empty;
    }

    /// <summary>イベント名・関数名のエイリアス定義。</summary>
    [Serializable]
    public sealed class PackageAliasManifest
    {
        [JsonProperty("events")]
        public Dictionary<string, string> Events = new Dictionary<string, string>();

        [JsonProperty("functions")]
        public Dictionary<string, string> Functions = new Dictionary<string, string>();
    }

    /// <summary>ディスク上のルートディレクトリとマニフェストを組み合わせた解決済みパッケージ。</summary>
    public sealed class ResolvedPackage
    {
        public ResolvedPackage(string rootDirectory, PackageManifest manifest)
        {
            RootDirectory = rootDirectory;
            Manifest = manifest;
        }

        public string RootDirectory { get; }
        public PackageManifest Manifest { get; }
        public string PackageId => Manifest.Id;

        public string GetAbsolutePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return System.IO.Path.GetFullPath(System.IO.Path.Combine(RootDirectory, relativePath));
        }
    }

    /// <summary>パッケージとオーバーライドから解決された最終的なコンテンツパス群。</summary>
    public sealed class PackageContentSelection
    {
        public List<string> DaihonPaths = new List<string>();
        public string CharacterPath = string.Empty;
        public Dictionary<string, string> TexturePaths = new Dictionary<string, string>();
        public Dictionary<string, string> AssetPaths = new Dictionary<string, string>();
        public Dictionary<string, string> MotionPaths = new Dictionary<string, string>();
        public List<string> DllPaths = new List<string>();
    }

    /// <summary>パッケージ検証結果(警告メッセージ一覧)。</summary>
    public sealed class PackageValidationReport
    {
        public readonly List<string> Warnings = new List<string>();

        public bool HasWarnings => Warnings.Count > 0;
    }
}
