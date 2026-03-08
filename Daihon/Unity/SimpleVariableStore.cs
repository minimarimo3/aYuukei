// ==========================================================================
// SimpleVariableStore.cs
// IVariableStore のシンプルな Dictionary ベース実装。
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Daihon;

namespace Daihon.Unity
{
    /// <summary>
    /// <see cref="IVariableStore"/> のシンプルな実装。
    /// 変数を Dictionary で管理し、型の再代入チェックと一時変数のスコープ管理を行います。
    /// 動的Getter（アクセスのたびに値が変わる変数）もサポートします（§9.3）。
    /// </summary>
    public class SimpleVariableStore : IVariableStore
    {
        private readonly Dictionary<string, DaihonValue> _variables = new Dictionary<string, DaihonValue>();

        // 動的変数。GetValue 呼び出し時に毎回 Func を評価する。
        // SetValue による上書きは禁止（§9.3参照）。
        private readonly Dictionary<string, Func<DaihonValue>> _dynamicGetters = new Dictionary<string, Func<DaihonValue>>();

        /// <summary>
        /// 動的Getterを登録する。
        /// TriggerManager.Start() で組み込み時間変数を登録する際に使用する（§9.3参照）。
        /// </summary>
        public void RegisterDynamicGetter(string name, Func<DaihonValue> getter)
        {
            _dynamicGetters[name] = getter;
        }

        /// <summary>変数が定義済みかどうか。</summary>
        public bool IsDefined(string name)
        {
            return _dynamicGetters.ContainsKey(name) || _variables.ContainsKey(name);
        }

        /// <summary>変数の値を取得する。動的Getterが登録されていれば優先して評価する。未定義の場合は例外をスローする。</summary>
        public DaihonValue GetValue(string name)
        {
            // 動的Getterが登録されていれば優先して評価する
            if (_dynamicGetters.TryGetValue(name, out var getter))
                return getter();

            if (_variables.TryGetValue(name, out var value))
                return value;

            throw new DaihonRuntimeException($"未定義の変数「{name}」が参照されました。");
        }

        /// <summary>
        /// 変数に値を代入する。
        /// 動的Getter登録済みの変数への上書きは禁止する（§9.3参照）。
        /// 既に定義済みの場合、型が異なる値への再代入は禁止する（仕様 §3.3）。
        /// </summary>
        public void SetValue(string name, DaihonValue value)
        {
            // 動的Getter登録済みの変数への上書きは禁止
            if (_dynamicGetters.ContainsKey(name))
                throw new InvalidOperationException($"動的変数 '{name}' には代入できません");

            if (_variables.TryGetValue(name, out var existing))
            {
                // 型の再代入チェック（None 型は任意の型に変更可能）
                if (existing.Type != DaihonValue.ValueType.None
                    && value.Type != DaihonValue.ValueType.None
                    && existing.Type != value.Type)
                {
                    throw new DaihonRuntimeException(
                        $"型エラー: 変数「{name}」は {existing.Type} 型ですが、{value.Type} 型の値を代入しようとしました。");
                }
            }

            _variables[name] = value;
        }

        /// <summary>
        /// 初期値を設定する（まだ定義されていない変数のみ）。
        /// 既に定義済みの場合は何もしない。
        /// </summary>
        public void SetDefaultValue(string name, DaihonValue value)
        {
            if (!_variables.ContainsKey(name))
                _variables[name] = value;
        }

        /// <summary>一時変数（_接頭辞）をすべて破棄する。</summary>
        public void ClearTemporaryVariables()
        {
            var tempKeys = _variables.Keys.Where(k => k.StartsWith("_")).ToList();
            foreach (var key in tempKeys)
                _variables.Remove(key);
        }

        /// <summary>すべての変数をクリアする（動的Getterは保持される）。</summary>
        public void ClearAll()
        {
            _variables.Clear();
        }
    }
}
