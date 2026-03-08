// ==========================================================================
// YuukeiActionHandler.cs
// DaihonScript の IUniTaskActionHandler 実装（§9.4）。
// 台本からの関数呼び出しをUnityコンポーネントに橋渡しする。
// ==========================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Daihon;
using Daihon.Unity;
using UnityEngine;

/// <summary>
/// DaihonScript の IUniTaskActionHandler 実装（§9.4）。
/// 台本からのセリフ表示・関数呼び出しをUnityコンポーネントに委譲する。
/// 未知の関数名は Debug.LogWarning を出して DaihonValue.None を返す（例外を投げない）。
/// </summary>
public class YuukeiActionHandler : MonoBehaviour, IUniTaskActionHandler
{
    [SerializeField] private CharacterManager characterManager;
    [SerializeField] private VRMLipSync lipSync;
    [SerializeField] private VrmaPlayer vrmaPlayer;
    [SerializeField] private FloatingMotionController floatingMotion;
    [SerializeField] private SpeechBubbleController speechBubble;
    [SerializeField] private LLMBridge llmBridge;

    /// <summary>セリフを表示し口パクを再生する。口パク完了まで await する（§9.4）。</summary>
    public async UniTask ShowDialogueAsync(string text)
    {
        if (speechBubble != null)
        {
            speechBubble.SetText(text);
            speechBubble.Show();
        }

        var ct = this.GetCancellationTokenOnDestroy();

        if (lipSync != null)
        {
            await lipSync.SpeakAsync(text, ct);
        }
        else
        {
            // 口パク未設定時はテキスト長に応じた待機
            float waitSeconds = Mathf.Max(1.0f, text.Length * 0.08f);
            await UniTask.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken: ct);
        }
    }

    /// <summary>
    /// 関数名に応じてディスパッチする。
    /// 組み込み関数に該当しない場合は DaihonFunctionRegistry を参照する。
    /// 未知の関数名は Debug.LogWarning を出して DaihonValue.None を返す（§9.4）。
    /// </summary>
    public async UniTask<DaihonValue> CallFunctionAsync(
        string functionName,
        IReadOnlyList<DaihonValue> positionalArgs,
        IReadOnlyDictionary<string, DaihonValue> namedArgs)
    {
        Debug.Log($"[YuukeiActionHandler] 関数呼び出し: 「{functionName}」");
        switch (functionName)
        {
            case "表情":
                return HandleExpression(positionalArgs);

            case "動作":
                return HandleMotion(positionalArgs);

            case "待つ":
            case "w":
                return await HandleWaitAsync(positionalArgs);

            case "ランダム":
                return HandleRandom(positionalArgs);

            case "現在の天気":
                return HandleCurrentWeather();

            case "LLM":
                return await HandleLLMAsync(positionalArgs);

            case "移動":
                return await HandleMoveAsync(positionalArgs);

            case "選択肢表示":
                return HandleChoices(positionalArgs);

            default:
                // DaihonFunctionRegistry を参照する
                if (DaihonFunctionRegistry.TryGet(functionName, out var customFunc))
                {
                    return await customFunc.CallAsync(positionalArgs, namedArgs);
                }
                Debug.LogWarning($"[YuukeiActionHandler] 未知の関数呼び出し: 「{functionName}」");
                return DaihonValue.None;
        }
    }

    // ── 組み込み関数実装 ──────────────────────────────────────────────────

    private DaihonValue HandleExpression(IReadOnlyList<DaihonValue> args)
    {
        if (args.Count == 0)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「表情」関数には表情名が必要です。");
            return DaihonValue.None;
        }

        var expressionName = args[0].AsString();
        if (characterManager?.CurrentVrmInstance != null)
        {
            try
            {
                characterManager.CurrentVrmInstance.Runtime.Expression.SetWeight(
                    UniVRM10.ExpressionKey.CreateCustom(expressionName), 1.0f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YuukeiActionHandler] 表情「{expressionName}」の設定に失敗: {e.Message}");
            }
        }
        return DaihonValue.None;
    }

    private DaihonValue HandleMotion(IReadOnlyList<DaihonValue> args)
    {
        if (args.Count == 0)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「動作」関数にはモーション名が必要です。");
            return DaihonValue.None;
        }

        var motionName = args[0].AsString();
        if (vrmaPlayer != null)
        {
            if (motionName == "停止")
            {
                vrmaPlayer.StopAnimation();
            }
            else
            {
                // TODO: motionName に基づくモーションの選択
                vrmaPlayer.PlayAnimation();
            }
        }
        return DaihonValue.None;
    }

    private async UniTask<DaihonValue> HandleWaitAsync(IReadOnlyList<DaihonValue> args)
    {
        if (args.Count == 0)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「待つ」関数には秒数が必要です。");
            return DaihonValue.None;
        }

        float seconds = (float)args[0].AsNumber();
        if (seconds > 0)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(seconds),
                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        return DaihonValue.None;
    }

    private DaihonValue HandleRandom(IReadOnlyList<DaihonValue> args)
    {
        if (args.Count < 2)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「ランダム」関数には最小値と最大値が必要です。");
            return DaihonValue.None;
        }

        float min = (float)args[0].AsNumber();
        float max = (float)args[1].AsNumber();
        double result = UnityEngine.Random.Range(min, max);
        return DaihonValue.FromNumber(result);
    }

    private static readonly string[] WeatherOptions = { "晴れ", "曇り", "雨", "雪" };

    private DaihonValue HandleCurrentWeather()
    {
        // TODO: 実際の天気APIと連携する
        return DaihonValue.FromString(WeatherOptions[UnityEngine.Random.Range(0, WeatherOptions.Length)]);
    }

    private async UniTask<DaihonValue> HandleLLMAsync(IReadOnlyList<DaihonValue> args)
    {
        if (args.Count == 0)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「LLM」関数にはプロンプトが必要です。");
            return DaihonValue.FromString("");
        }

        var prompt = args[0].AsString();
        var bridge = llmBridge != null ? llmBridge : LLMBridge.Instance;
        if (bridge == null)
        {
            Debug.LogWarning("[YuukeiActionHandler] LLMBridge が見つかりません。");
            return DaihonValue.FromString("");
        }

        var response = await bridge.ChatAsync(prompt);
        return DaihonValue.FromString(response);
    }

        private async UniTask<DaihonValue> HandleMoveAsync(IReadOnlyList<DaihonValue> args)
    {
        if (floatingMotion == null)
        {
            Debug.LogWarning("[YuukeiActionHandler] 「移動」: FloatingMotionController が未設定です。");
            return DaihonValue.None;
        }

        Vector3 targetPos = Camera.main.ViewportToWorldPoint(new Vector3(floatingMotion.AnchorPosition.x, floatingMotion.AnchorPosition.y, 0));

        // 移動の引数はビューポートの座標(0~1000)で指定する
        if (args.Count >= 1) {
            targetPos.x = Camera.main.ViewportToWorldPoint(new Vector3((float)(args[0].AsNumber() / 1000.0), targetPos.y, 0)).x;
        }
        if (args.Count >= 2) {
            targetPos.y = Camera.main.ViewportToWorldPoint(new Vector3(targetPos.x, (float)(args[1].AsNumber() / 1000.0), 0)).y;
        }
        
        Debug.Log($"[YuukeiActionHandler] 移動: 「{targetPos.x}, {targetPos.y}」");
        var target = new Vector3(targetPos.x, targetPos.y, floatingMotion.AnchorPosition.z);

        await floatingMotion.MoveToAsync(target, this.GetCancellationTokenOnDestroy());
        return DaihonValue.None;
    }


    private DaihonValue HandleChoices(IReadOnlyList<DaihonValue> args)
    {
        // TODO: 選択UIを表示する実装
        Debug.Log("[YuukeiActionHandler] 「選択肢表示」は未実装です。");
        return DaihonValue.None;
    }
}
