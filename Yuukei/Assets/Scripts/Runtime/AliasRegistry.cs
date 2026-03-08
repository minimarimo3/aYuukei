using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yuukei.Runtime
{
    public sealed class AliasRegistry
    {
        private sealed class AliasEntry
        {
            public AliasEntry(string canonicalName, int priority, string sourceName)
            {
                CanonicalName = canonicalName;
                Priority = priority;
                SourceName = sourceName;
            }

            public string CanonicalName { get; }
            public int Priority { get; }
            public string SourceName { get; }
        }

        private const int BuiltinPriority = 0;
        private const int PackagePriority = 1;

        private readonly Dictionary<string, AliasEntry> _eventAliases = new Dictionary<string, AliasEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, AliasEntry> _functionAliases = new Dictionary<string, AliasEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _canonicalEvents = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _canonicalFunctions = new Dictionary<string, string>(StringComparer.Ordinal);

        public AliasRegistry()
        {
            ResetToBuiltins();
        }

        public void ResetToBuiltins()
        {
            _eventAliases.Clear();
            _functionAliases.Clear();
            _canonicalEvents.Clear();
            _canonicalFunctions.Clear();

            RegisterEventAliasInternal("起動時", "app_started", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("クリック", "character_clicked", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("ダブルクリック", "character_double_clicked", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("ドラッグ開始", "character_drag_started", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("ドラッグ終了", "character_drag_ended", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("ファイルドロップ", "file_dropped", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("放置", "idle_reached", BuiltinPriority, "builtin");
            RegisterEventAliasInternal("定期発火", "periodic_tick", BuiltinPriority, "builtin");

            RegisterFunctionAliasInternal("吹き出し表示", "show_dialog", BuiltinPriority, "builtin");
            RegisterFunctionAliasInternal("表情変更", "set_expression", BuiltinPriority, "builtin");
            RegisterFunctionAliasInternal("モーション再生", "play_motion", BuiltinPriority, "builtin");
            RegisterFunctionAliasInternal("小物表示", "set_prop_visible", BuiltinPriority, "builtin");
            RegisterFunctionAliasInternal("選択肢表示", "show_choices", BuiltinPriority, "builtin");
            RegisterFunctionAliasInternal("永続保存", "set_persistent", BuiltinPriority, "builtin");
        }

        public void LoadPackageAliases(PackageAliasManifest aliases)
        {
            aliases ??= new PackageAliasManifest();

            foreach (var pair in aliases.Events)
            {
                RegisterEventAliasInternal(pair.Key, pair.Value, PackagePriority, "package");
            }

            foreach (var pair in aliases.Functions)
            {
                RegisterFunctionAliasInternal(pair.Key, pair.Value, PackagePriority, "package");
            }
        }

        public void RegisterEventAlias(string alias, string canonicalName)
        {
            RegisterEventAliasInternal(alias, canonicalName, PackagePriority, "runtime");
        }

        public void RegisterFunctionAlias(string alias, string canonicalName)
        {
            RegisterFunctionAliasInternal(alias, canonicalName, PackagePriority, "runtime");
        }

        public bool TryResolveEventName(string rawName, out string canonicalName)
        {
            return TryResolve(_eventAliases, _canonicalEvents, rawName, out canonicalName);
        }

        public bool TryResolveFunctionName(string rawName, out string canonicalName)
        {
            return TryResolve(_functionAliases, _canonicalFunctions, rawName, out canonicalName);
        }

        internal IReadOnlyDictionary<string, string> GetEventAliasSnapshot()
        {
            var snapshot = new Dictionary<string, string>();
            foreach (var pair in _eventAliases)
            {
                snapshot[pair.Key] = pair.Value.CanonicalName;
            }

            return snapshot;
        }

        internal IReadOnlyDictionary<string, string> GetFunctionAliasSnapshot()
        {
            var snapshot = new Dictionary<string, string>();
            foreach (var pair in _functionAliases)
            {
                snapshot[pair.Key] = pair.Value.CanonicalName;
            }

            return snapshot;
        }

        private void RegisterEventAliasInternal(string alias, string canonicalName, int priority, string sourceName)
        {
            RegisterAlias(_eventAliases, _canonicalEvents, alias, canonicalName, priority, sourceName, "event");
        }

        private void RegisterFunctionAliasInternal(string alias, string canonicalName, int priority, string sourceName)
        {
            RegisterAlias(_functionAliases, _canonicalFunctions, alias, canonicalName, priority, sourceName, "function");
        }

        private static bool TryResolve(
            Dictionary<string, AliasEntry> aliasEntries,
            Dictionary<string, string> canonicalEntries,
            string rawName,
            out string canonicalName)
        {
            canonicalName = string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            var normalized = Normalize(rawName);
            if (aliasEntries.TryGetValue(normalized, out var aliasEntry))
            {
                canonicalName = aliasEntry.CanonicalName;
                return true;
            }

            return canonicalEntries.TryGetValue(normalized, out canonicalName);
        }

        private static void RegisterAlias(
            Dictionary<string, AliasEntry> aliasEntries,
            Dictionary<string, string> canonicalEntries,
            string alias,
            string canonicalName,
            int priority,
            string sourceName,
            string kind)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(canonicalName))
            {
                return;
            }

            var trimmedCanonical = canonicalName.Trim();
            canonicalEntries[Normalize(trimmedCanonical)] = trimmedCanonical;

            var normalizedAlias = Normalize(alias);
            if (aliasEntries.TryGetValue(normalizedAlias, out var existing)
                && !string.Equals(existing.CanonicalName, trimmedCanonical, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[AliasRegistry] {kind} alias collision: '{alias}' was '{existing.CanonicalName}' from {existing.SourceName}, now '{trimmedCanonical}' from {sourceName}.");
                if (priority < existing.Priority)
                {
                    return;
                }
            }

            aliasEntries[normalizedAlias] = new AliasEntry(trimmedCanonical, priority, sourceName);
        }

        internal static string Normalize(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            if (IsAscii(trimmed))
            {
                return trimmed.ToLowerInvariant();
            }

            return trimmed;
        }

        private static bool IsAscii(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] > 127)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
