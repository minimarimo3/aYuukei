// ==========================================================================
// UniTaskActionAdapter.cs
// DaihonScript の IActionHandler（Task ベース）と Unity 側の
// IUniTaskActionHandler（UniTask ベース）を橋渡しするアダプター。
// ==========================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Daihon;

namespace Daihon.Unity
{
    // ================================================================
    // ユーザーが実装するインターフェース（UniTask ベース）
    // ================================================================

    /// <summary>
    /// UniTask を使用したアクションハンドラーインターフェース。
    /// Unity 側ではこのインターフェースを実装してください。
    /// </summary>
    public interface IUniTaskActionHandler
    {
        /// <summary>セリフを表示する（表示待ち・クリック待ちを含む）。</summary>
        UniTask ShowDialogueAsync(string text);

        /// <summary>
        /// 関数を呼び出す。
        /// 戻り値を持つ場合はその値を、持たない場合は DaihonValue.None を返す。
        /// </summary>
        /// <param name="functionName">関数名</param>
        /// <param name="positionalArgs">位置引数のリスト</param>
        /// <param name="namedArgs">名前付き引数の辞書</param>
        UniTask<DaihonValue> CallFunctionAsync(
            string functionName,
            IReadOnlyList<DaihonValue> positionalArgs,
            IReadOnlyDictionary<string, DaihonValue> namedArgs);
    }

    // ================================================================
    // 内部アダプター（Daihon.dll の IActionHandler を実装）
    // ================================================================

    /// <summary>
    /// <see cref="IUniTaskActionHandler"/>（UniTask）を
    /// <see cref="IActionHandler"/>（Task）に変換するアダプター。
    /// <see cref="DaihonRunner"/> 内部で自動的に使用されます。
    /// </summary>
    internal sealed class UniTaskActionAdapter : IActionHandler
    {
        private readonly IUniTaskActionHandler _handler;

        public UniTaskActionAdapter(IUniTaskActionHandler handler)
        {
            _handler = handler;
        }

        Task IActionHandler.ShowDialogueAsync(string text)
        {
            return _handler.ShowDialogueAsync(text).AsTask();
        }

        Task<DaihonValue> IActionHandler.CallFunctionAsync(
            string functionName,
            IReadOnlyList<DaihonValue> positionalArgs,
            IReadOnlyDictionary<string, DaihonValue> namedArgs)
        {
            return _handler.CallFunctionAsync(functionName, positionalArgs, namedArgs).AsTask();
        }
    }
}
