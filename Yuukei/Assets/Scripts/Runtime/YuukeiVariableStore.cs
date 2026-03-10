using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Daihon;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 台本ランタイム用の変数ストア。
    /// 永続変数・一時変数・動的変数・イベントコンテキスト変数を統合管理する。
    /// </summary>
    public sealed class YuukeiVariableStore : IVariableStore
    {
        private readonly Dictionary<string, DaihonValue> _variables = new Dictionary<string, DaihonValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<DaihonValue>> _dynamicGetters = new Dictionary<string, Func<DaihonValue>>(StringComparer.Ordinal);
        private readonly PersistenceStore _persistenceStore;

        public YuukeiVariableStore(PersistenceStore persistenceStore)
        {
            _persistenceStore = persistenceStore;
            RegisterBuiltinTimeVariables();
            LoadPersistentVariables();
            Debug.Log($"[YuukeiVariableStore] 初期化完了 (永続変数={_variables.Count}, 動的変数={_dynamicGetters.Count})");
        }

        public bool IsDefined(string name)
        {
            return _dynamicGetters.ContainsKey(name) || _variables.ContainsKey(name);
        }

        public DaihonValue GetValue(string name)
        {
            if (_dynamicGetters.TryGetValue(name, out var getter))
            {
                return getter();
            }

            if (_variables.TryGetValue(name, out var value))
            {
                return value;
            }

            throw new DaihonRuntimeException($"未定義の変数「{name}」が参照されました。");
        }

        public void SetValue(string name, DaihonValue value)
        {
            if (_dynamicGetters.ContainsKey(name))
            {
                throw new DaihonRuntimeException($"動的変数 '{name}' には代入できません。");
            }

            if (_variables.TryGetValue(name, out var existing)
                && existing.Type != DaihonValue.ValueType.None
                && value.Type != DaihonValue.ValueType.None
                && existing.Type != value.Type)
            {
                throw new DaihonRuntimeException($"型エラー: 変数「{name}」は {existing.Type} 型ですが、{value.Type} 型の値を代入しようとしました。");
            }

            _variables[name] = value;
            if (!name.StartsWith("_", StringComparison.Ordinal))
            {
                Debug.Log($"[YuukeiVariableStore] 永続変数を設定: {name}={value}");
                _persistenceStore.SetPersistentVariable(name, DaihonValueUtility.ToPersistentObject(value));
                _persistenceStore.RequestSave();
            }
        }

        /// <summary>変数が未定義の場合のみデフォルト値を設定する。</summary>
        public void SetDefaultValue(string name, DaihonValue value)
        {
            if (IsDefined(name))
            {
                return;
            }

            Debug.Log($"[YuukeiVariableStore] デフォルト値を設定: {name}={value}");
            _variables[name] = value;
            if (!name.StartsWith("_", StringComparison.Ordinal))
            {
                _persistenceStore.SetPersistentVariable(name, DaihonValueUtility.ToPersistentObject(value));
                _persistenceStore.RequestSave();
            }
        }

        public void ClearTemporaryVariables()
        {
            var keysToRemove = new List<string>();
            foreach (var pair in _variables)
            {
                if (pair.Key.StartsWith("_", StringComparison.Ordinal))
                {
                    keysToRemove.Add(pair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _variables.Remove(key);
            }
        }

        /// <summary>イベント発火時のコンテキスト変数を注入する。</summary>
        public void InjectEventContext(IReadOnlyDictionary<string, object> context)
        {
            ClearEventContext();
            if (context == null)
            {
                return;
            }

            foreach (var pair in context)
            {
                _variables[pair.Key] = DaihonValueUtility.ToDaihonValue(pair.Value);
            }

            Debug.Log($"[YuukeiVariableStore] イベントコンテキスト注入: {context.Count} 件");
        }

        public void ClearEventContext()
        {
            var keysToRemove = new List<string>();
            foreach (var pair in _variables)
            {
                if (pair.Key.StartsWith("_event_", StringComparison.Ordinal))
                {
                    keysToRemove.Add(pair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _variables.Remove(key);
            }
        }

        /// <summary>一時変数とイベントコンテキストをすべてクリアする。</summary>
        public void ResetTransientState()
        {
            Debug.Log("[YuukeiVariableStore] 一時状態をリセット");
            ClearTemporaryVariables();
            ClearEventContext();
        }

        public void RegisterDynamicGetter(string name, Func<DaihonValue> getter)
        {
            _dynamicGetters[name] = getter;
        }

        private void LoadPersistentVariables()
        {
            foreach (var pair in _persistenceStore.GetPersistentVariablesSnapshot())
            {
                _variables[pair.Key] = DaihonValueUtility.ToDaihonValue(pair.Value);
            }
        }

        private void RegisterBuiltinTimeVariables()
        {
            RegisterDynamicGetter("年", () => DaihonValue.FromNumber(DateTime.Now.Year));
            RegisterDynamicGetter("月", () => DaihonValue.FromNumber(DateTime.Now.Month));
            RegisterDynamicGetter("日", () => DaihonValue.FromNumber(DateTime.Now.Day));
            RegisterDynamicGetter("時", () => DaihonValue.FromNumber(DateTime.Now.Hour));
            RegisterDynamicGetter("分", () => DaihonValue.FromNumber(DateTime.Now.Minute));
            RegisterDynamicGetter("秒", () => DaihonValue.FromNumber(DateTime.Now.Second));
            RegisterDynamicGetter("ミリ秒", () => DaihonValue.FromNumber(DateTime.Now.Millisecond));
            RegisterDynamicGetter("曜日", () => DaihonValue.FromString(new[] { "日", "月", "火", "水", "木", "金", "土" }[(int)DateTime.Now.DayOfWeek]));
            RegisterDynamicGetter("週", () =>
            {
                var culture = CultureInfo.CurrentCulture;
                var calendar = culture.Calendar;
                var week = calendar.GetWeekOfYear(DateTime.Now, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                return DaihonValue.FromNumber(week);
            });
        }
    }
}
