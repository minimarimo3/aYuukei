using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// パッケージ内の DLL プラグインを検出・承認・読み込みするクラス。
    /// ユーザーが明示的に承認するまで DLL は読み込まれず、安全性を保つ。
    /// </summary>
    public sealed class PluginLoader
    {
        public sealed class PluginCandidate
        {
            public PluginCandidate(string path)
            {
                Path = path ?? string.Empty;
                FileName = string.IsNullOrWhiteSpace(path) ? string.Empty : System.IO.Path.GetFileName(path);
            }

            public string Path { get; }
            public string FileName { get; }
            public bool Exists => !string.IsNullOrWhiteSpace(Path) && File.Exists(Path);
            public bool IsApproved { get; internal set; }
            public bool IsActivated { get; internal set; }
            public string LastError { get; internal set; } = string.Empty;
        }

        private readonly Dictionary<string, PluginCandidate> _candidates = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _approvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event Action StateChanged;

        public IReadOnlyList<PluginCandidate> Candidates => _candidates.Values.OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase).ToArray();

        public bool HasPendingApproval
        {
            get
            {
                foreach (var candidate in _candidates.Values)
                {
                    if (candidate.Exists && !candidate.IsApproved)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>指定された DLL パス一覧を候補として登録する。</summary>
        public void Scan(IEnumerable<string> dllPaths)
        {
            _candidates.Clear();
            if (dllPaths != null)
            {
                foreach (var dllPath in dllPaths)
                {
                    if (string.IsNullOrWhiteSpace(dllPath))
                    {
                        continue;
                    }

                    var fullPath = System.IO.Path.GetFullPath(dllPath);
                    var candidate = new PluginCandidate(fullPath)
                    {
                        IsApproved = _approvedPaths.Contains(fullPath),
                    };
                    _candidates[fullPath] = candidate;
                }
            }

            Debug.Log($"[PluginLoader] DLL スキャン完了: {_candidates.Count}件の候補を検出");
            NotifyChanged();
        }

        /// <summary>未承認の全候補を一括承認する。</summary>
        public void ApproveAllPending()
        {
            var pendingCount = _candidates.Values.Count(c => c.Exists && !c.IsApproved);
            Debug.Log($"[PluginLoader] 未承認プラグインを一括承認します: {pendingCount}件");
            foreach (var candidate in _candidates.Values)
            {
                if (!candidate.Exists)
                {
                    continue;
                }

                candidate.IsApproved = true;
                _approvedPaths.Add(candidate.Path);
            }

            NotifyChanged();
        }

        /// <summary>全承認をクリアし、プラグインを無効化状態に戻す。</summary>
        public void ClearApprovals()
        {
            Debug.Log("[PluginLoader] 全承認をクリアします");
            _approvedPaths.Clear();
            foreach (var candidate in _candidates.Values)
            {
                candidate.IsApproved = false;
                candidate.IsActivated = false;
                candidate.LastError = string.Empty;
            }

            NotifyChanged();
        }

        /// <summary>承認済みプラグインを実際にアセンブリとして読み込む。</summary>
        public void ActivateApprovedPlugins()
        {
            Debug.Log("[PluginLoader] 承認済みプラグインの有効化を開始します");
            foreach (var candidate in _candidates.Values)
            {
                if (!candidate.Exists || !candidate.IsApproved || candidate.IsActivated)
                {
                    continue;
                }

                try
                {
                    Debug.Log($"[PluginLoader] プラグイン読み込み中: {candidate.FileName}");
                    Assembly.LoadFrom(candidate.Path);
                    candidate.IsActivated = true;
                    candidate.LastError = string.Empty;
                    Debug.Log($"[PluginLoader] プラグイン有効化成功: {candidate.FileName}");
                }
                catch (Exception exception)
                {
                    candidate.IsActivated = false;
                    candidate.LastError = exception.Message;
                    Debug.LogWarning($"[PluginLoader] Failed to load plugin '{candidate.Path}': {exception.Message}");
                }
            }

            NotifyChanged();
        }

        public string BuildWarningText()
        {
            if (_candidates.Count == 0)
            {
                return "DLL は見つかっていません。";
            }

            return "DLL は危険を伴う拡張です。自動では読み込まれません。内容を信頼できる場合のみ明示的に承認してください。";
        }

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
