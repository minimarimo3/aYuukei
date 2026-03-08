using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Yuukei.Runtime
{
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

    [Serializable]
    public sealed class OverrideSelections
    {
        [JsonProperty("daihon")]
        public string Daihon = string.Empty;

        [JsonProperty("character")]
        public string Character = string.Empty;

        [JsonProperty("textures")]
        public Dictionary<string, string> Textures = new Dictionary<string, string>();

        [JsonProperty("assets")]
        public Dictionary<string, string> Assets = new Dictionary<string, string>();

        [JsonProperty("motions")]
        public Dictionary<string, string> Motions = new Dictionary<string, string>();

        public void Reset()
        {
            Daihon = string.Empty;
            Character = string.Empty;
            Textures.Clear();
            Assets.Clear();
            Motions.Clear();
        }
    }

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
        public string Daihon = string.Empty;

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
            Textures ??= new Dictionary<string, PackageTextureManifest>();
            Assets ??= new List<string>();
            Motions ??= new List<string>();
            Dlls ??= new List<string>();
            Aliases ??= new PackageAliasManifest();
        }
    }

    [Serializable]
    public sealed class PackageTextureManifest
    {
        [JsonProperty("background")]
        public string Background = string.Empty;

        [JsonProperty("tail")]
        public string Tail = string.Empty;
    }

    [Serializable]
    public sealed class PackageAliasManifest
    {
        [JsonProperty("events")]
        public Dictionary<string, string> Events = new Dictionary<string, string>();

        [JsonProperty("functions")]
        public Dictionary<string, string> Functions = new Dictionary<string, string>();
    }

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

    public sealed class PackageContentSelection
    {
        public string DaihonPath = string.Empty;
        public string CharacterPath = string.Empty;
        public Dictionary<string, string> TexturePaths = new Dictionary<string, string>();
        public Dictionary<string, string> AssetPaths = new Dictionary<string, string>();
        public Dictionary<string, string> MotionPaths = new Dictionary<string, string>();
        public List<string> DllPaths = new List<string>();
    }

    public sealed class PackageValidationReport
    {
        public readonly List<string> Warnings = new List<string>();

        public bool HasWarnings => Warnings.Count > 0;
    }
}
