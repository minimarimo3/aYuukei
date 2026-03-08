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
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PersistenceStore] Failed to load save.json. Recreating defaults. {exception.Message}");
                Data = YuukeiSaveData.CreateDefault();
            }
        }

        public async UniTask SaveAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_saveFilePath) ?? Application.persistentDataPath);
            await _saveLock.WaitAsync(cancellationToken);
            try
            {
                var json = JsonConvert.SerializeObject(Data, Formatting.Indented);
                await File.WriteAllTextAsync(_saveFilePath, json, cancellationToken);
            }
            finally
            {
                _saveLock.Release();
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
