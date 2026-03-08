using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Yuukei.Runtime
{
    public sealed class PersistenceStore
    {
        private readonly string _saveFilePath;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly object _saveStateLock = new object();
        private int _requestedSaveVersion;
        private int _flushedSaveVersion;
        private bool _saveLoopActive;

        public PersistenceStore(string saveFilePath = null)
        {
            _saveFilePath = saveFilePath ?? Path.Combine(Application.persistentDataPath, "save.json");
            Data = YuukeiSaveData.CreateDefault();
        }

        public YuukeiSaveData Data { get; private set; }
        public string SaveFilePath => _saveFilePath;

        public async UniTask LoadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_saveFilePath))
            {
                Data = YuukeiSaveData.CreateDefault();
                ResetSaveState();
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_saveFilePath, cancellationToken);
                var root = JObject.Parse(json);
                var loaded = YuukeiSaveData.CreateDefault();

                loaded.ActivePackageId = root.Value<string>("activePackageId") ?? string.Empty;
                loaded.Overrides = root["overrides"]?.ToObject<OverrideSelections>() ?? new OverrideSelections();
                loaded.Overrides.Normalize();
                loaded.AppState = root["appState"]?.ToObject<AppStateData>() ?? new AppStateData();
                loaded.PersistentVariables = new Dictionary<string, object>();

                if (root["persistentVariables"] is JObject persistentObject)
                {
                    foreach (var property in persistentObject.Properties())
                    {
                        if (!TryConvertPersistentToken(property.Value, out var value))
                        {
                            Debug.LogWarning($"[PersistenceStore] Ignoring persistent variable '{property.Name}' because its type is not supported.");
                            continue;
                        }

                        loaded.PersistentVariables[property.Name] = value;
                    }
                }

                Data = loaded;
                ResetSaveState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PersistenceStore] Failed to load save.json. Recreating defaults. {exception.Message}");
                Data = YuukeiSaveData.CreateDefault();
                ResetSaveState();
            }
        }

        public async UniTask SaveAsync(CancellationToken cancellationToken = default)
        {
            int targetVersion;
            lock (_saveStateLock)
            {
                targetVersion = _requestedSaveVersion;
            }

            await SaveCoreAsync(cancellationToken);
            lock (_saveStateLock)
            {
                if (_flushedSaveVersion < targetVersion)
                {
                    _flushedSaveVersion = targetVersion;
                }
            }
        }

        public void SaveImmediately()
        {
            int targetVersion;
            lock (_saveStateLock)
            {
                targetVersion = _requestedSaveVersion;
            }

            SaveCore(CancellationToken.None);
            lock (_saveStateLock)
            {
                if (_flushedSaveVersion < targetVersion)
                {
                    _flushedSaveVersion = targetVersion;
                }
            }
        }

        public void RequestSave()
        {
            lock (_saveStateLock)
            {
                _requestedSaveVersion++;
                if (_saveLoopActive)
                {
                    return;
                }

                _saveLoopActive = true;
            }

            ProcessQueuedSavesAsync().Forget();
        }

        public async UniTask FlushPendingSaveAsync(CancellationToken cancellationToken = default)
        {
            int targetVersion;
            lock (_saveStateLock)
            {
                targetVersion = _requestedSaveVersion;
                if (targetVersion <= _flushedSaveVersion)
                {
                    return;
                }
            }

            await SaveCoreAsync(cancellationToken);
            lock (_saveStateLock)
            {
                if (_flushedSaveVersion < targetVersion)
                {
                    _flushedSaveVersion = targetVersion;
                }
            }
        }

        public IReadOnlyDictionary<string, object> GetPersistentVariablesSnapshot()
        {
            return new Dictionary<string, object>(Data.PersistentVariables);
        }

        public bool TryGetPersistentVariable(string key, out object value)
        {
            return Data.PersistentVariables.TryGetValue(key, out value);
        }

        public void SetPersistentVariable(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Persistent variable key must not be empty.", nameof(key));
            }

            if (!DaihonValueUtility.IsPersistentType(value))
            {
                throw new InvalidOperationException("Persistent variables must be bool, number, or string.");
            }

            if (Data.PersistentVariables.TryGetValue(key, out var existing))
            {
                var existingType = DaihonValueUtility.GetPersistentType(existing);
                var nextType = DaihonValueUtility.GetPersistentType(value);
                if (existingType != nextType)
                {
                    throw new InvalidOperationException($"Persistent variable '{key}' cannot change type from {existingType.Name} to {nextType.Name}.");
                }
            }

            Data.PersistentVariables[key] = NormalizePersistentValue(value);
        }

        public void SetActivePackageId(string packageId)
        {
            Data.ActivePackageId = packageId ?? string.Empty;
        }

        public void ResetOverrides()
        {
            Data.Overrides.Reset();
        }

        public void SetOverrides(OverrideSelections overrides)
        {
            Data.Overrides = overrides ?? new OverrideSelections();
            Data.Overrides.Normalize();
        }

        public void UpdateAppState(Action<AppStateData> update)
        {
            update?.Invoke(Data.AppState);
        }

        private async UniTask SaveCoreAsync(CancellationToken cancellationToken)
        {
            await UniTask.SwitchToThreadPool();
            SaveCore(cancellationToken);
            await UniTask.SwitchToMainThread(cancellationToken);
        }

        private void SaveCore(CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(_saveFilePath) ?? Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            _saveLock.Wait(cancellationToken);
            var temporaryPath = _saveFilePath + ".tmp";
            try
            {
                var json = JsonConvert.SerializeObject(Data, Formatting.Indented);
                cancellationToken.ThrowIfCancellationRequested();
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(_saveFilePath))
                {
                    File.Replace(temporaryPath, _saveFilePath, null);
                }
                else
                {
                    File.Move(temporaryPath, _saveFilePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                _saveLock.Release();
            }
        }

        private async UniTaskVoid ProcessQueuedSavesAsync()
        {
            try
            {
                while (true)
                {
                    await UniTask.Yield();

                    int targetVersion;
                    lock (_saveStateLock)
                    {
                        targetVersion = _requestedSaveVersion;
                        if (targetVersion <= _flushedSaveVersion)
                        {
                            _saveLoopActive = false;
                            return;
                        }
                    }

                    await SaveCoreAsync(CancellationToken.None);
                    lock (_saveStateLock)
                    {
                        if (_flushedSaveVersion < targetVersion)
                        {
                            _flushedSaveVersion = targetVersion;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PersistenceStore] Background save failed. {exception.Message}");
                lock (_saveStateLock)
                {
                    _saveLoopActive = false;
                }
            }
        }

        private void ResetSaveState()
        {
            lock (_saveStateLock)
            {
                _requestedSaveVersion = 0;
                _flushedSaveVersion = 0;
                _saveLoopActive = false;
            }
        }

        private static object NormalizePersistentValue(object value)
        {
            return value switch
            {
                byte byteValue => Convert.ToDouble(byteValue, CultureInfo.InvariantCulture),
                sbyte sbyteValue => Convert.ToDouble(sbyteValue, CultureInfo.InvariantCulture),
                short shortValue => Convert.ToDouble(shortValue, CultureInfo.InvariantCulture),
                ushort ushortValue => Convert.ToDouble(ushortValue, CultureInfo.InvariantCulture),
                int intValue => Convert.ToDouble(intValue, CultureInfo.InvariantCulture),
                uint uintValue => Convert.ToDouble(uintValue, CultureInfo.InvariantCulture),
                long longValue => Convert.ToDouble(longValue, CultureInfo.InvariantCulture),
                ulong ulongValue => Convert.ToDouble(ulongValue, CultureInfo.InvariantCulture),
                float floatValue => Convert.ToDouble(floatValue, CultureInfo.InvariantCulture),
                decimal decimalValue => Convert.ToDouble(decimalValue, CultureInfo.InvariantCulture),
                _ => value
            };
        }

        private static bool TryConvertPersistentToken(JToken token, out object value)
        {
            value = null;
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    value = token.Value<bool>();
                    return true;
                case JTokenType.Integer:
                case JTokenType.Float:
                    value = token.Value<double>();
                    return true;
                case JTokenType.String:
                    value = token.Value<string>() ?? string.Empty;
                    return true;
                default:
                    return false;
            }
        }
    }
}
