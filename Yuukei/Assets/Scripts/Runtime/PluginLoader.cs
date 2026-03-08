using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Yuukei.Runtime
{
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

            NotifyChanged();
        }

        public void ApproveAllPending()
        {
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

        public void ClearApprovals()
        {
            _approvedPaths.Clear();
            foreach (var candidate in _candidates.Values)
            {
                candidate.IsApproved = false;
                candidate.IsActivated = false;
                candidate.LastError = string.Empty;
            }

            NotifyChanged();
        }

        public void ActivateApprovedPlugins()
        {
            foreach (var candidate in _candidates.Values)
            {
                if (!candidate.Exists || !candidate.IsApproved || candidate.IsActivated)
                {
                    continue;
                }

                try
                {
                    Assembly.LoadFrom(candidate.Path);
                    candidate.IsActivated = true;
                    candidate.LastError = string.Empty;
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
