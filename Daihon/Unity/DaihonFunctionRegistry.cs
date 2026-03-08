// ==========================================================================
// DaihonFunctionRegistry.cs
// プラグインが追加するカスタム台本関数を管理するシングルトン（§9.5）。
// ==========================================================================

using System.Collections.Generic;
using Daihon.Unity;
using UnityEngine;

/// <summary>
/// プラグインが追加するカスタム台本関数を管理するシングルトン（§9.5）。
/// </summary>
public static class DaihonFunctionRegistry
{
    private static readonly Dictionary<string, IDaihonFunction> _functions =
        new Dictionary<string, IDaihonFunction>();

    /// <summary>カスタム関数を登録する。同名の関数が既に存在する場合は上書きする。</summary>
    public static void Register(IDaihonFunction function)
    {
        if (function == null)
        {
            Debug.LogWarning("[DaihonFunctionRegistry] null の関数は登録できません。");
            return;
        }
        _functions[function.FunctionName] = function;
    }

    /// <summary>指定した名前のカスタム関数を登録解除する。</summary>
    public static void Unregister(string functionName)
    {
        _functions.Remove(functionName);
    }

    /// <summary>指定した名前のカスタム関数を取得する。</summary>
    public static bool TryGet(string functionName, out IDaihonFunction function)
    {
        return _functions.TryGetValue(functionName, out function);
    }
}
