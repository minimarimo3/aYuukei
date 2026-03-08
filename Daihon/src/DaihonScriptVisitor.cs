using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Daihon
{
    // ================================================================
    // DaihonValue — 台本言語の値を型安全に保持するラッパー型
    // ================================================================

    /// <summary>
    /// 台本言語で扱う3つの型（数値・文字列・真偽値）と「値なし」を表現する。
    /// 参照型として実装し、ジェネリック制約との互換性を確保する。
    /// </summary>
    public sealed class DaihonValue
    {
        public enum ValueType { None, Number, String, Boolean }

        public ValueType Type { get; }

        private readonly double _number;
        private readonly string _string;
        private readonly bool _boolean;

        // ---- シングルトン / ファクトリ ----

        public static readonly DaihonValue None = new(ValueType.None, 0, "", false);
        public static readonly DaihonValue True = new(ValueType.Boolean, 0, "", true);
        public static readonly DaihonValue False = new(ValueType.Boolean, 0, "", false);

        private DaihonValue(ValueType type, double number, string str, bool boolean)
        {
            Type = type;
            _number = number;
            _string = str;
            _boolean = boolean;
        }

        public static DaihonValue FromNumber(double value) => new(ValueType.Number, value, "", false);
        public static DaihonValue FromString(string value) => new(ValueType.String, 0, value ?? "", false);
        public static DaihonValue FromBoolean(bool value) => value ? True : False;

        // ---- 値の取得 ----

        public double AsNumber()
        {
            if (Type != ValueType.Number)
                throw new DaihonRuntimeException($"型エラー: {Type} を数値として取得できません。");
            return _number;
        }

        public string AsString()
        {
            if (Type != ValueType.String)
                throw new DaihonRuntimeException($"型エラー: {Type} を文字列として取得できません。");
            return _string;
        }

        public bool AsBoolean()
        {
            if (Type != ValueType.Boolean)
                throw new DaihonRuntimeException($"型エラー: {Type} を真偽値として取得できません。");
            return _boolean;
        }

        /// <summary>真偽値として評価する（省略形条件用）。真偽値型は直接評価、それ以外はエラー。</summary>
        public bool IsTruthy()
        {
            return Type switch
            {
                ValueType.Boolean => _boolean,
                _ => throw new DaihonRuntimeException(
                    $"型エラー: {Type} 型の値を真偽値として評価できません。")
            };
        }

        /// <summary>表示用の文字列変換。</summary>
        public string ToDisplayString()
        {
            return Type switch
            {
                ValueType.Number => IsInteger() ? ((long)_number).ToString() : _number.ToString(CultureInfo.InvariantCulture),
                ValueType.String => _string,
                ValueType.Boolean => _boolean ? "はい" : "いいえ",
                _ => ""
            };
        }

        /// <summary>内部の数値が整数かどうか。</summary>
        public bool IsInteger() => Type == ValueType.Number && Math.Abs(_number - Math.Truncate(_number)) < double.Epsilon;

        public override string ToString() => $"DaihonValue({Type}: {ToDisplayString()})";

        // ---- 等価比較 ----

        public override bool Equals(object obj)
        {
            if (obj is not DaihonValue other) return false;
            if (Type != other.Type) return false;
            return Type switch
            {
                ValueType.Number => Math.Abs(_number - other._number) < double.Epsilon,
                ValueType.String => _string == other._string,
                ValueType.Boolean => _boolean == other._boolean,
                _ => true
            };
        }

        public override int GetHashCode()
        {
            return Type switch
            {
                ValueType.Number => HashCode.Combine(Type, _number),
                ValueType.String => HashCode.Combine(Type, _string),
                ValueType.Boolean => HashCode.Combine(Type, _boolean),
                _ => HashCode.Combine(Type)
            };
        }
    }

    // ================================================================
    // 例外クラス
    // ================================================================

    /// <summary>台本の実行時エラー。</summary>
    public class DaihonRuntimeException : Exception
    {
        public DaihonRuntimeException(string message) : base(message) { }
        public DaihonRuntimeException(string message, Exception inner) : base(message, inner) { }
    }

    // ================================================================
    // 制御フロー用の内部例外（Visitor内部でのみ使用）
    // ================================================================

    /// <summary>→おわり でイベント全体を終了する。</summary>
    internal class EventEndException : Exception { }

    /// <summary>→シーンおわり で現在のシーンを終了する。</summary>
    internal class SceneEndException : Exception { }

    /// <summary>→シーン名 で指定シーンにジャンプする。</summary>
    internal class SceneJumpException : Exception
    {
        public string TargetSceneName { get; }
        public SceneJumpException(string name) => TargetSceneName = name;
    }

    // ================================================================
    // インターフェース
    // ================================================================

    /// <summary>
    /// セリフ表示・関数呼び出し（アクション）を外部エンジンに委譲するインターフェース。
    /// </summary>
    public interface IActionHandler
    {
        /// <summary>セリフを表示する（表示待ちを含む）。</summary>
        Task ShowDialogueAsync(string text);

        /// <summary>
        /// 関数を呼び出す。
        /// 戻り値を持つ場合はその値を、持たない場合は DaihonValue.None を返す。
        /// </summary>
        /// <param name="functionName">関数名</param>
        /// <param name="positionalArgs">位置引数のリスト</param>
        /// <param name="namedArgs">名前付き引数の辞書</param>
        Task<DaihonValue> CallFunctionAsync(
            string functionName,
            IReadOnlyList<DaihonValue> positionalArgs,
            IReadOnlyDictionary<string, DaihonValue> namedArgs);
    }

    /// <summary>
    /// 変数の読み書きを外部ストアに委譲するインターフェース。
    /// 永続変数と一時変数（_接頭辞）のスコープ管理もこのストアで行う。
    /// </summary>
    public interface IVariableStore
    {
        /// <summary>変数が定義済みかどうか。</summary>
        bool IsDefined(string name);

        /// <summary>変数の値を取得する。未定義の場合は例外をスローする。</summary>
        DaihonValue GetValue(string name);

        /// <summary>
        /// 変数に値を代入する。
        /// 型の再代入チェックもこのメソッド内で行う。
        /// </summary>
        void SetValue(string name, DaihonValue value);

        /// <summary>
        /// 初期値を設定する（まだ定義されていない変数のみ）。
        /// 既に定義済みの場合は何もしない。
        /// </summary>
        void SetDefaultValue(string name, DaihonValue value);

        /// <summary>一時変数（_接頭辞）をすべて破棄する。</summary>
        void ClearTemporaryVariables();
    }

    // ================================================================
    // DaihonScriptVisitor — 構文木を直接走査するインタープリタ
    // ================================================================

    /// <summary>
    /// ANTLRが生成した DaihonParser の構文木を Visitor パターンで走査し、
    /// 台本を即座に実行するインタープリタ。
    /// すべてのルールメソッドを明示的にオーバーライドし、VisitChildren は使用しない。
    /// </summary>
    public class DaihonScriptVisitor : DaihonParserBaseVisitor<Task<DaihonValue>>
    {
        private readonly IActionHandler _actionHandler;
        private readonly IVariableStore _variableStore;
        private int _jumpCount;

        /// <summary>ジャンプ回数の上限（無限ループ防止）。</summary>
        private const int MaxJumpCount = 1000;

        // スレッドごとに独立したRandomインスタンスを生成する
        // シード値にGuidを使うことで、複数スレッド同時初期化時のシード被りを防ぐ
        private static readonly ThreadLocal<Random> _threadLocalRng = 
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        public DaihonScriptVisitor(IActionHandler actionHandler, IVariableStore variableStore)
        {
            _actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
            _variableStore = variableStore ?? throw new ArgumentNullException(nameof(variableStore));
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>NUMBERトークンのテキストを double に変換する。</summary>
        private static double ParseNumber(string text)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(text[2..], 16);
            if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(text[2..], 8);
            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(text[2..], 2);
            // 全角数字・全角ピリオドを半角に変換
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '\uFF10' && c <= '\uFF19')
                    sb.Append((char)(c - '\uFF10' + '0'));
                else if (c == '\uFF0E')
                    sb.Append('.');
                else
                    sb.Append(c);
            }
            return double.Parse(sb.ToString(), CultureInfo.InvariantCulture);
        }

        /// <summary>TIMEトークン (hh:mm) を分単位の合計に変換する。</summary>
        private static int ParseTimeToMinutes(string timeText)
        {
            // 全角数字・全角コロンを半角に変換
            var sb = new System.Text.StringBuilder(timeText.Length);
            foreach (char c in timeText)
            {
                if (c >= '\uFF10' && c <= '\uFF19')
                    sb.Append((char)(c - '\uFF10' + '0'));
                else if (c == '\uFF1A')
                    sb.Append(':');
                else
                    sb.Append(c);
            }

            var parts = sb.ToString().Split(':');
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }

        /// <summary>atomノードから変数名を抽出する（代入の左辺用）。</summary>
        private static string ExtractVariableName(DaihonParser.AtomContext atom)
        {
            if (atom.NUMBER() != null)
            {
                var parts = new List<string> { atom.NUMBER().GetText() };
                foreach (var id in atom.IDENTIFIER())
                    parts.Add(id.GetText());
                return string.Join("", parts);
            }
            // IDENTIFIER() は配列を返すので、最初の要素を取得
            if (atom.IDENTIFIER().Length > 0)
                return atom.IDENTIFIER()[0].GetText();

            throw new DaihonRuntimeException(
                $"行{atom.Start.Line}: 代入の左辺には変数名を指定してください。");
        }

        /// <summary>現在時刻を分単位で取得する（テスト用にオーバーライド可能）。</summary>
        protected virtual int GetCurrentTimeInMinutes()
        {
            var now = DateTime.Now;
            return now.Hour * 60 + now.Minute;
        }

        // ================================================================
        // file（トップレベル）
        // ================================================================

        public override async Task<DaihonValue> VisitFile(DaihonParser.FileContext context)
        {
            await VisitEventDecl(context.eventDecl());
            return DaihonValue.None;
        }

        // ================================================================
        // イベント宣言（§2.1）
        // ================================================================

        public override async Task<DaihonValue> VisitEventDecl(DaihonParser.EventDeclContext context)
        {
            try
            {
                // 初期値ブロック
                // 前提条件ブロックの判定で初期値を利用できるようにするため、先に変数を登録します。
                if (context.defaultsBlock() != null)
                    await VisitDefaultsBlock(context.defaultsBlock());

                // 前提条件ブロック
                if (context.preconditionBlock() != null)
                {
                    try
                    {
                        await VisitPreconditionBlock(context.preconditionBlock());
                    }
                    catch (EventEndException)
                    {
                        _variableStore.ClearTemporaryVariables();
                        return DaihonValue.None;
                    }
                }

                // シーンの実行
                var scenes = context.scene();
                await ExecuteScenesAsync(scenes);

                _variableStore.ClearTemporaryVariables();
            }
            // 「→おわり」でイベント全体を終了
            catch (EventEndException)
            {
            }
            finally
            {
                _variableStore.ClearTemporaryVariables();
            }
            return DaihonValue.None;
        }

        /// <summary>
        /// シーンの順次実行・ジャンプ制御を行うメインループ。
        /// §2.6.5 のデフォルトシーン選択ロジックも含む。
        /// </summary>
        private async Task ExecuteScenesAsync(DaihonParser.SceneContext[] scenes)
        {
            _jumpCount = 0; // 実行開始時にリセット

            bool anyTriggeredSceneExecuted = false;
            var defaultScenes = new List<DaihonParser.SceneContext>();

            for (int i = 0; i < scenes.Length; i++)
            {
                var scene = scenes[i];
                bool hasSignal = scene.signalDecl() != null;
                bool hasCondition = scene.conditionDecl() != null;

                if (!hasSignal && !hasCondition)
                {
                    defaultScenes.Add(scene);
                    continue;
                }

                if (hasCondition)
                {
                    var condResult = await EvaluateCondExpr(scene.conditionDecl().condExpr());
                    if (!condResult)
                        continue;
                }

                anyTriggeredSceneExecuted = true;
                try
                {
                    await ExecuteSceneBody(scene);
                }
                catch (EventEndException)
                {
                    return;
                }
                catch (SceneEndException)
                {
                    continue;
                }
                catch (SceneJumpException jump)
                {
                    _jumpCount++;
                    if (_jumpCount > MaxJumpCount)
                        throw new DaihonRuntimeException("ジャンプ回数が上限（1000回）を超えました。無限ループの可能性があります。");

                    await ExecuteJumpAsync(scenes, jump.TargetSceneName);
                    return;
                }
            }

            if (!anyTriggeredSceneExecuted && defaultScenes.Count > 0)
            {
                var chosen = defaultScenes[_threadLocalRng.Value!.Next(defaultScenes.Count)];
                try
                {
                    await ExecuteSceneBody(chosen);
                }
                catch (EventEndException)
                {
                    return;
                }
                catch (SceneEndException)
                {
                    // デフォルトシーン終了
                }
                catch (SceneJumpException jump)
                {
                    _jumpCount++;
                    if (_jumpCount > MaxJumpCount)
                        throw new DaihonRuntimeException("ジャンプ回数が上限（1000回）を超えました。無限ループの可能性があります。");
                    await ExecuteJumpAsync(scenes, jump.TargetSceneName);
                }
            }
        }

        /// <summary>ジャンプ先のシーンを見つけて実行する。ジャンプ先からさらにジャンプする可能性がある。</summary>
        private async Task ExecuteJumpAsync(DaihonParser.SceneContext[] allScenes, string targetName)
        {
            string currentTarget = targetName;

            while (true)
            {
                var target = allScenes.FirstOrDefault(s => s.HEADER_NAME().GetText().Trim() == currentTarget);
                if (target == null)
                    throw new DaihonRuntimeException($"ジャンプ先のシーン「{currentTarget}」が見つかりません。");

                try
                {
                    await ExecuteSceneBody(target);
                    return;
                }
                catch (EventEndException)
                {
                    throw;
                }
                catch (SceneEndException)
                {
                    return;
                }
                catch (SceneJumpException jump)
                {
                    _jumpCount++;
                    if (_jumpCount > MaxJumpCount)
                        throw new DaihonRuntimeException("ジャンプ回数が上限（1000回）を超えました。無限ループの可能性があります。");
                    currentTarget = jump.TargetSceneName;
                }
            }
        }

        /// <summary>シーンの本体（stmtList）を実行する。</summary>
        private async Task ExecuteSceneBody(DaihonParser.SceneContext scene)
        {
            await VisitStmtList(scene.stmtList());
        }

        // ================================================================
        // 前提条件ブロック（§2.2）
        // ================================================================

        public override async Task<DaihonValue> VisitPreconditionBlock(DaihonParser.PreconditionBlockContext context)
        {
            foreach (var jump in context.conditionalJump())
                await VisitConditionalJump(jump);
            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitConditionalJump(DaihonParser.ConditionalJumpContext context)
        {
            bool condResult = await EvaluateCondExpr(context.condExpr());
            if (condResult)
            {
                // ジャンプ先を評価して制御例外をスロー
                await ExecuteJumpTarget(context.jumpTarget());
            }
            return DaihonValue.None;
        }

        // ================================================================
        // 初期値ブロック（§2.3）
        // ================================================================

        public override async Task<DaihonValue> VisitDefaultsBlock(DaihonParser.DefaultsBlockContext context)
        {
            foreach (var assign in context.assignment())
            {
                var name = ExtractVariableName(assign.atom());
                var value = await EvaluateExpr(assign.expr());
                _variableStore.SetDefaultValue(name, value);
            }
            return DaihonValue.None;
        }

        // ================================================================
        // シーン（§2.5, §2.6）
        // ================================================================

        public override async Task<DaihonValue> VisitScene(DaihonParser.SceneContext context)
        {
            // ExecuteScenesAsync から呼ばれるため、ここでは本体のみ実行
            await VisitStmtList(context.stmtList());
            return DaihonValue.None;
        }

        // 合図宣言・条件宣言はデータ読み取り用。直接実行時は上位で処理済み。
        public override Task<DaihonValue> VisitSignalDecl(DaihonParser.SignalDeclContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitSystemEventList(DaihonParser.SystemEventListContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitSystemEvent(DaihonParser.SystemEventContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitConditionDecl(DaihonParser.ConditionDeclContext context)
            => Task.FromResult(DaihonValue.None);

        // ================================================================
        // 文リスト・文（§6.1）
        // ================================================================

        public override async Task<DaihonValue> VisitStmtList(DaihonParser.StmtListContext context)
        {
            foreach (var stmtWithNewline in context.children ?? Enumerable.Empty<IParseTree>())
            {
                if (stmtWithNewline is DaihonParser.StmtContext stmt)
                    await VisitStmt(stmt);
                // NEWLINE トークンは無視
            }
            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitStmt(DaihonParser.StmtContext context)
        {
            if (context.displaySequence() != null)
                return await VisitDisplaySequence(context.displaySequence());
            if (context.assignment() != null)
                return await VisitAssignment(context.assignment());
            if (context.jump() != null)
                return await VisitJump(context.jump());
            if (context.conditionalStmt() != null)
                return await VisitConditionalStmt(context.conditionalStmt());
            return DaihonValue.None;
        }

        // ================================================================
        // 表示列（§6.1）
        // ================================================================

        public override async Task<DaihonValue> VisitDisplaySequence(DaihonParser.DisplaySequenceContext context)
        {
            foreach (var element in context.displayElement())
                await VisitDisplayElement(element);
            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitDisplayElement(DaihonParser.DisplayElementContext context)
        {
            if (context.dialogue() != null)
                return await VisitDialogue(context.dialogue());
            if (context.funcCall() != null)
                return await VisitFuncCall(context.funcCall());
            return DaihonValue.None;
        }

        // ================================================================
        // 代入（§3）
        // ================================================================

        public override async Task<DaihonValue> VisitAssignment(DaihonParser.AssignmentContext context)
        {
            var name = ExtractVariableName(context.atom());
            var value = await EvaluateExpr(context.expr());
            _variableStore.SetValue(name, value);
            return DaihonValue.None;
        }

        // ================================================================
        // ジャンプ（§7）
        // ================================================================

        public override async Task<DaihonValue> VisitJump(DaihonParser.JumpContext context)
        {
            await ExecuteJumpTarget(context.jumpTarget());
            return DaihonValue.None; // 到達しない（例外でフロー制御）
        }

        public override Task<DaihonValue> VisitJumpTarget(DaihonParser.JumpTargetContext context)
        {
            // ExecuteJumpTarget で直接処理するため、ここは呼ばれない
            return Task.FromResult(DaihonValue.None);
        }

        /// <summary>ジャンプ先に応じた制御例外をスローする。</summary>
        private Task ExecuteJumpTarget(DaihonParser.JumpTargetContext target)
        {
            if (target.OWARI() != null)
                throw new EventEndException();
            if (target.SCENE_OWARI() != null)
                throw new SceneEndException();

            // シーン名へのジャンプ
            var sceneNameParts = target.jumpName()
                .Select(jn => jn.GetText())
                .ToArray();
            var sceneName = string.Join(" ", sceneNameParts);
            throw new SceneJumpException(sceneName);
        }

        public override Task<DaihonValue> VisitJumpName(DaihonParser.JumpNameContext context)
            => Task.FromResult(DaihonValue.None);

        // ================================================================
        // 条件付き文（§6.1, §6.2, §6.3）
        // ================================================================

        public override async Task<DaihonValue> VisitConditionalStmt(DaihonParser.ConditionalStmtContext context)
        {
            // ブロック記法: ※（条件）: ... elseIfBlock* elseBlock? おわり
            if (context.COLON() != null)
            {
                // メインの if 条件を評価
                bool mainCondResult = await EvaluateCondExpr(context.condExpr());
                if (mainCondResult)
                {
                    await VisitStmtList(context.stmtList());
                    return DaihonValue.None;
                }

                // else if ブランチ
                foreach (var elseIf in context.elseIfBlock())
                {
                    bool elseIfResult = await EvaluateCondExpr(elseIf.condExpr());
                    if (elseIfResult)
                    {
                        await VisitStmtList(elseIf.stmtList());
                        return DaihonValue.None;
                    }
                }

                // else ブランチ
                if (context.elseBlock() != null)
                    await VisitStmtList(context.elseBlock().stmtList());

                return DaihonValue.None;
            }

            // 1行記法: ※（条件）singleAction
            if (context.singleAction() != null)
            {
                bool condResult = await EvaluateCondExpr(context.condExpr());
                if (condResult)
                    await VisitSingleAction(context.singleAction());
                return DaihonValue.None;
            }

            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitElseIfBlock(DaihonParser.ElseIfBlockContext context)
        {
            // VisitConditionalStmt 内で直接処理するため通常は呼ばれない
            await VisitStmtList(context.stmtList());
            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitElseBlock(DaihonParser.ElseBlockContext context)
        {
            // VisitConditionalStmt 内で直接処理するため通常は呼ばれない
            await VisitStmtList(context.stmtList());
            return DaihonValue.None;
        }

        public override async Task<DaihonValue> VisitSingleAction(DaihonParser.SingleActionContext context)
        {
            if (context.dialogue() != null)
                return await VisitDialogue(context.dialogue());
            if (context.funcCall() != null)
                return await VisitFuncCall(context.funcCall());
            if (context.assignment() != null)
                return await VisitAssignment(context.assignment());
            if (context.jump() != null)
                return await VisitJump(context.jump());
            return DaihonValue.None;
        }

        // ================================================================
        // 条件式（§4）
        // ================================================================

        /// <summary>condExpr を評価して bool を返す統合メソッド。</summary>
        private async Task<bool> EvaluateCondExpr(DaihonParser.CondExprContext context)
        {
            return await EvaluateCondOrExpr(context.condOrExpr());
        }

        private async Task<bool> EvaluateCondOrExpr(DaihonParser.CondOrExprContext context)
        {
            var andExprs = context.condAndExpr();
            bool result = await EvaluateCondAndExpr(andExprs[0]);
            for (int i = 1; i < andExprs.Length; i++)
            {
                // 短絡評価: すでに true なら残りを評価しない
                if (result) return true;
                result = await EvaluateCondAndExpr(andExprs[i]);
            }
            return result;
        }

        private async Task<bool> EvaluateCondAndExpr(DaihonParser.CondAndExprContext context)
        {
            var primaries = context.condPrimary();
            bool result = await EvaluateCondPrimary(primaries[0]);
            for (int i = 1; i < primaries.Length; i++)
            {
                // 短絡評価: すでに false なら残りを評価しない
                if (!result) return false;
                result = await EvaluateCondPrimary(primaries[i]);
            }
            return result;
        }

        private async Task<bool> EvaluateCondPrimary(DaihonParser.CondPrimaryContext context)
        {
            // 括弧グループ: （condExpr）
            if (context.condExpr() != null)
                return await EvaluateCondExpr(context.condExpr());

            // 時間範囲: TIME~TIME 等
            if (context.timeRange() != null)
                return EvaluateTimeRange(context.timeRange());

            var exprs = context.expr();

            // 範囲比較: expr rangeOp
            if (context.rangeOp() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                return await EvaluateRangeOp(leftValue, context.rangeOp());
            }

            // 後置比較: expr postfixCompOp
            if (context.postfixCompOp() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                return await EvaluatePostfixCompOp(leftValue, context.postfixCompOp());
            }

            // 中置比較: expr infixCompOp expr
            if (context.infixCompOp() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                var rightValue = await EvaluateExpr(exprs[1]);
                return EvaluateInfixCompOp(leftValue, context.infixCompOp(), rightValue);
            }

            // 中置等価: expr EQ expr
            if (context.EQ() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                var rightValue = await EvaluateExpr(exprs[1]);
                return EvaluateEquality(leftValue, rightValue);
            }

            // 中置不等価: expr NEQ expr
            if (context.NEQ() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                var rightValue = await EvaluateExpr(exprs[1]);
                return !EvaluateEquality(leftValue, rightValue);
            }

            // 後置等価: expr ASSIGN_EQ expr（条件式内の=は等価比較）
            if (context.ASSIGN_EQ() != null)
            {
                var leftValue = await EvaluateExpr(exprs[0]);
                var rightValue = await EvaluateExpr(exprs[1]);
                return EvaluateEquality(leftValue, rightValue);
            }

            // 真偽値省略形: expr（変数名のみ → =はい と同等）
            if (exprs.Length == 1)
            {
                var value = await EvaluateExpr(exprs[0]);
                return value.IsTruthy();
            }

            throw new DaihonRuntimeException(
                $"行{context.Start.Line}: 未対応の条件式パターンです。");
        }

        // ---- Visitor 形式のオーバーライド（EvaluateCondXxx から直接使わないが、網羅性のため定義） ----

        public override async Task<DaihonValue> VisitCondExpr(DaihonParser.CondExprContext context)
        {
            bool result = await EvaluateCondExpr(context);
            return DaihonValue.FromBoolean(result);
        }

        public override async Task<DaihonValue> VisitCondOrExpr(DaihonParser.CondOrExprContext context)
        {
            bool result = await EvaluateCondOrExpr(context);
            return DaihonValue.FromBoolean(result);
        }

        public override async Task<DaihonValue> VisitCondAndExpr(DaihonParser.CondAndExprContext context)
        {
            bool result = await EvaluateCondAndExpr(context);
            return DaihonValue.FromBoolean(result);
        }

        public override async Task<DaihonValue> VisitCondPrimary(DaihonParser.CondPrimaryContext context)
        {
            bool result = await EvaluateCondPrimary(context);
            return DaihonValue.FromBoolean(result);
        }

        // ---- 比較演算の評価 ----

        /// <summary>型安全な等価比較（§3.5: 異なる型同士は常に偽）。</summary>
        private static bool EvaluateEquality(DaihonValue left, DaihonValue right)
        {
            if (left.Type != right.Type) return false;
            return left.Equals(right);
        }

        /// <summary>後置比較: atom (未満|以下|以上|超える)</summary>
        private async Task<bool> EvaluatePostfixCompOp(DaihonValue leftValue, DaihonParser.PostfixCompOpContext op)
        {
            var rightValue = await EvaluateAtom(op.atom());

            if (leftValue.Type != DaihonValue.ValueType.Number || rightValue.Type != DaihonValue.ValueType.Number)
                return false; // 型不一致は偽

            double l = leftValue.AsNumber();
            double r = rightValue.AsNumber();

            if (op.MIMAN() != null) return l < r;
            if (op.IKA() != null) return l <= r;
            if (op.IJOU() != null) return l >= r;
            if (op.KOERU() != null) return l > r;

            return false;
        }

        /// <summary>中置比較: < <= > >=</summary>
        private static bool EvaluateInfixCompOp(DaihonValue left, DaihonParser.InfixCompOpContext op, DaihonValue right)
        {
            if (left.Type != DaihonValue.ValueType.Number || right.Type != DaihonValue.ValueType.Number)
                return false;

            double l = left.AsNumber();
            double r = right.AsNumber();

            if (op.LT() != null) return l < r;
            if (op.LTE() != null) return l <= r;
            if (op.GT() != null) return l > r;
            if (op.GTE() != null) return l >= r;

            return false;
        }

        /// <summary>範囲比較: atom? ~ atom?（§4.4）</summary>
        private async Task<bool> EvaluateRangeOp(DaihonValue leftValue, DaihonParser.RangeOpContext range)
        {
            if (leftValue.Type != DaihonValue.ValueType.Number)
                return false;

            double val = leftValue.AsNumber();
            var atoms = range.atom();

            if (atoms.Length == 2)
            {
                // 両端指定: low ~ high
                var low = await EvaluateAtom(atoms[0]);
                var high = await EvaluateAtom(atoms[1]);
                return val >= low.AsNumber() && val <= high.AsNumber();
            }
            else if (atoms.Length == 1)
            {
                // TILDE の前後どちらに atom があるかで片側省略を判定
                var atom = atoms[0];
                int tildeIndex = -1;
                int atomIndex = -1;
                for (int i = 0; i < range.ChildCount; i++)
                {
                    var child = range.GetChild(i);
                    if (child is ITerminalNode tn && tn.Symbol.Type == DaihonLexer.TILDE)
                        tildeIndex = i;
                    if (child == atom)
                        atomIndex = i;
                }

                var bound = await EvaluateAtom(atom);
                if (atomIndex < tildeIndex)
                {
                    // atom ~ → 以上
                    return val >= bound.AsNumber();
                }
                else
                {
                    // ~ atom → 以下
                    return val <= bound.AsNumber();
                }
            }
            else
            {
                // ~ のみ（両側省略）→ 常に真
                return true;
            }
        }

        /// <summary>時間範囲の評価（§4.6）。</summary>
        private bool EvaluateTimeRange(DaihonParser.TimeRangeContext context)
        {
            int currentMinutes = GetCurrentTimeInMinutes();
            var times = context.TIME();

            if (times.Length == 2)
            {
                // TIME ~ TIME
                int from = ParseTimeToMinutes(times[0].GetText());
                int to = ParseTimeToMinutes(times[1].GetText());
                return currentMinutes >= from && currentMinutes <= to;
            }
            else if (times.Length == 1)
            {
                int time = ParseTimeToMinutes(times[0].GetText());
                // TILDE の位置で判定
                if (context.GetChild(0) is ITerminalNode tn && tn.Symbol.Type == DaihonLexer.TILDE)
                {
                    // ~TIME → 以下
                    return currentMinutes <= time;
                }
                else
                {
                    // TIME~ → 以上
                    return currentMinutes >= time;
                }
            }

            return false;
        }

        public override Task<DaihonValue> VisitPostfixCompOp(DaihonParser.PostfixCompOpContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitInfixCompOp(DaihonParser.InfixCompOpContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitRangeOp(DaihonParser.RangeOpContext context)
            => Task.FromResult(DaihonValue.None);

        public override Task<DaihonValue> VisitTimeRange(DaihonParser.TimeRangeContext context)
            => Task.FromResult(DaihonValue.FromBoolean(EvaluateTimeRange(context)));

        // ================================================================
        // 算術式（§3.6）
        // ================================================================

        /// <summary>expr を評価して DaihonValue を返す統合メソッド。</summary>
        private async Task<DaihonValue> EvaluateExpr(DaihonParser.ExprContext context)
        {
            var unaryExprs = context.unaryExpr();
            var result = await EvaluateUnaryExpr(unaryExprs[0]);

            // 演算子トークンの位置を追跡
            int opIndex = 0;
            for (int i = 1; i < unaryExprs.Length; i++)
            {
                // unaryExpr 間の演算子を見つける
                ITerminalNode opNode = FindOperatorBetween(context, unaryExprs[i - 1], unaryExprs[i]);
                var right = await EvaluateUnaryExpr(unaryExprs[i]);

                if (opNode.Symbol.Type == DaihonLexer.PLUS)
                    result = PerformAdd(result, right, context.Start.Line);
                else if (opNode.Symbol.Type == DaihonLexer.MINUS)
                    result = PerformSubtract(result, right, context.Start.Line);
            }

            return result;
        }

        /// <summary>2つの子ノード間にある演算子トークンを見つける。</summary>
        private static ITerminalNode FindOperatorBetween(ParserRuleContext parent, IParseTree left, IParseTree right)
        {
            bool passedLeft = false;
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == left) { passedLeft = true; continue; }
                if (child == right) break;
                if (passedLeft && child is ITerminalNode tn)
                    return tn;
            }
            throw new DaihonRuntimeException("内部エラー: 演算子トークンが見つかりません。");
        }

        private async Task<DaihonValue> EvaluateUnaryExpr(DaihonParser.UnaryExprContext context)
        {
            var value = await EvaluateMulExpr(context.mulExpr());

            // 単項演算子
            if (context.MINUS() != null)
            {
                if (value.Type != DaihonValue.ValueType.Number)
                    throw new DaihonRuntimeException($"行{context.Start.Line}: 単項マイナスは数値にのみ適用できます。");
                return DaihonValue.FromNumber(-value.AsNumber());
            }
            // 単項プラスはそのまま
            return value;
        }

        private async Task<DaihonValue> EvaluateMulExpr(DaihonParser.MulExprContext context)
        {
            var atoms = context.atom();
            var result = await EvaluateAtom(atoms[0]);

            for (int i = 1; i < atoms.Length; i++)
            {
                var opNode = FindOperatorBetween(context, atoms[i - 1], atoms[i]);
                var right = await EvaluateAtom(atoms[i]);

                if (result.Type != DaihonValue.ValueType.Number || right.Type != DaihonValue.ValueType.Number)
                    throw new DaihonRuntimeException(
                        $"行{context.Start.Line}: 乗除算は数値同士でのみ使用できます。");

                double l = result.AsNumber();
                double r = right.AsNumber();

                result = opNode.Symbol.Type switch
                {
                    DaihonLexer.STAR => DaihonValue.FromNumber(l * r),
                    DaihonLexer.SLASH => PerformDivide(l, r, result.IsInteger() && right.IsInteger(), context.Start.Line),
                    DaihonLexer.PERCENT => PerformModulo(l, r, context.Start.Line),
                    _ => throw new DaihonRuntimeException($"行{context.Start.Line}: 未知の乗除算演算子です。")
                };
            }

            return result;
        }

        // ---- 演算ヘルパー ----

        /// <summary>加算 or 文字列結合（§3.6.1）。</summary>
        private static DaihonValue PerformAdd(DaihonValue left, DaihonValue right, int line)
        {
            // 両方数値 → 加算
            if (left.Type == DaihonValue.ValueType.Number && right.Type == DaihonValue.ValueType.Number)
                return DaihonValue.FromNumber(left.AsNumber() + right.AsNumber());

            // 片方でも文字列 → 文字列結合
            if (left.Type == DaihonValue.ValueType.String || right.Type == DaihonValue.ValueType.String)
                return DaihonValue.FromString(left.ToDisplayString() + right.ToDisplayString());

            throw new DaihonRuntimeException(
                $"行{line}: + 演算子は数値同士の加算、または文字列を含む結合でのみ使用できます（{left.Type} + {right.Type}）。");
        }

        /// <summary>減算。</summary>
        private static DaihonValue PerformSubtract(DaihonValue left, DaihonValue right, int line)
        {
            if (left.Type != DaihonValue.ValueType.Number || right.Type != DaihonValue.ValueType.Number)
                throw new DaihonRuntimeException($"行{line}: 減算は数値同士でのみ使用できます。");
            return DaihonValue.FromNumber(left.AsNumber() - right.AsNumber());
        }

        /// <summary>除算（§3.6: 整数同士は切り捨て）。</summary>
        private static DaihonValue PerformDivide(double l, double r, bool bothInteger, int line)
        {
            if (Math.Abs(r) < double.Epsilon)
                throw new DaihonRuntimeException($"行{line}: 0で除算することはできません。");

            if (bothInteger)
                return DaihonValue.FromNumber(Math.Truncate(l / r));
            return DaihonValue.FromNumber(l / r);
        }

        /// <summary>剰余。</summary>
        private static DaihonValue PerformModulo(double l, double r, int line)
        {
            if (Math.Abs(r) < double.Epsilon)
                throw new DaihonRuntimeException($"行{line}: 0で剰余を取ることはできません。");
            return DaihonValue.FromNumber(l % r);
        }

        // ---- Visitor形式のオーバーライド ----

        public override async Task<DaihonValue> VisitExpr(DaihonParser.ExprContext context)
            => await EvaluateExpr(context);

        public override async Task<DaihonValue> VisitUnaryExpr(DaihonParser.UnaryExprContext context)
            => await EvaluateUnaryExpr(context);

        public override async Task<DaihonValue> VisitMulExpr(DaihonParser.MulExprContext context)
            => await EvaluateMulExpr(context);

        // ================================================================
        // atom（§3.6）
        // ================================================================

        /// <summary>atom を評価して DaihonValue を返す。</summary>
        private async Task<DaihonValue> EvaluateAtom(DaihonParser.AtomContext context)
        {
            if (context.NUMBER() != null)
            {
                var identifiers = context.IDENTIFIER();
                if (identifiers != null && identifiers.Length > 0)
                {
                    var name = context.NUMBER().GetText() + string.Join("", identifiers.Select(id => id.GetText()));
                    return _variableStore.GetValue(name);
                }
                else
                {
                    return DaihonValue.FromNumber(ParseNumber(context.NUMBER().GetText()));
                }
            }

            // IDENTIFIER() は配列を返すので、最初の要素を取得
            if (context.IDENTIFIER().Length > 0)
                return _variableStore.GetValue(context.IDENTIFIER()[0].GetText());

            if (context.HAI() != null)
                return DaihonValue.True;

            if (context.IIE() != null)
                return DaihonValue.False;

            if (context.stringLiteral() != null)
                return await VisitStringLiteral(context.stringLiteral());

            if (context.funcCall() != null)
                return await VisitFuncCall(context.funcCall());

            if (context.expr() != null)
                return await EvaluateExpr(context.expr());

            throw new DaihonRuntimeException($"行{context.Start.Line}: 未対応の atom パターンです。");
        }

        public override async Task<DaihonValue> VisitAtom(DaihonParser.AtomContext context)
            => await EvaluateAtom(context);

        // ================================================================
        // 関数呼び出し（§5）
        // ================================================================

        public override async Task<DaihonValue> VisitFuncCall(DaihonParser.FuncCallContext context)
        {
            var funcName = context.funcName().IDENTIFIER().GetText();

            var positionalArgs = new List<DaihonValue>();
            var namedArgs = new Dictionary<string, DaihonValue>();

            foreach (var arg in context.funcArg())
            {
                if (arg.ASSIGN_EQ() != null)
                {
                    // 名前付き引数: IDENTIFIER = funcArgValue
                    var argName = arg.IDENTIFIER().GetText();
                    var argValue = await EvaluateFuncArgValue(arg.funcArgValue());
                    namedArgs[argName] = argValue;
                }
                else
                {
                    // 位置引数
                    var argValue = await EvaluateFuncArgValue(arg.funcArgValue());
                    positionalArgs.Add(argValue);
                }
            }

            return await _actionHandler.CallFunctionAsync(funcName, positionalArgs, namedArgs);
        }

        public override async Task<DaihonValue> VisitFuncName(DaihonParser.FuncNameContext context)
            => DaihonValue.FromString(context.IDENTIFIER().GetText());

        public override async Task<DaihonValue> VisitFuncArg(DaihonParser.FuncArgContext context)
        {
            // VisitFuncCall で直接処理するため通常は呼ばれない
            return await EvaluateFuncArgValue(context.funcArgValue());
        }

        /// <summary>関数引数の値を評価する。</summary>
        private async Task<DaihonValue> EvaluateFuncArgValue(DaihonParser.FuncArgValueContext context)
        {
            if (context.NUMBER() != null)
                return DaihonValue.FromNumber(ParseNumber(context.NUMBER().GetText()));
            if (context.HAI() != null)
                return DaihonValue.True;
            if (context.IIE() != null)
                return DaihonValue.False;
            if (context.stringLiteral() != null)
                return await VisitStringLiteral(context.stringLiteral());
            if (context.IDENTIFIER() != null)
                return _variableStore.GetValue(context.IDENTIFIER().GetText());
            if (context.expr() != null)
                return await EvaluateExpr(context.expr());

            throw new DaihonRuntimeException($"行{context.Start.Line}: 未対応の関数引数パターンです。");
        }

        public override async Task<DaihonValue> VisitFuncArgValue(DaihonParser.FuncArgValueContext context)
            => await EvaluateFuncArgValue(context);

        // ================================================================
        // セリフ（§6.4）
        // ================================================================

        public override async Task<DaihonValue> VisitDialogue(DaihonParser.DialogueContext context)
        {
            var text = await BuildDialogueText(context.dialogueContent());
            await _actionHandler.ShowDialogueAsync(text);
            return DaihonValue.None;
        }

        // ================================================================
        // 文字列リテラル
        // ================================================================

        public override async Task<DaihonValue> VisitStringLiteral(DaihonParser.StringLiteralContext context)
        {
            var text = await BuildDialogueText(context.dialogueContent());
            return DaihonValue.FromString(text);
        }

        // ================================================================
        // セリフ内容の構築（dialogue / stringLiteral 共通）
        // ================================================================

        /// <summary>
        /// dialogueContent の配列からテキストを構築する。
        /// プレーンテキスト、エスケープ、改行はそのまま文字列に。
        /// 関数呼び出しは実行して戻り値を展開する。
        /// </summary>
        private async Task<string> BuildDialogueText(DaihonParser.DialogueContentContext[] contents)
        {
            if (contents == null || contents.Length == 0)
                return "";

            var parts = new List<string>();
            foreach (var content in contents)
            {
                parts.Add(await BuildSingleDialogueContent(content));
            }
            return string.Join("", parts);
        }

        private async Task<string> BuildSingleDialogueContent(DaihonParser.DialogueContentContext content)
        {
            // プレーンテキスト
            if (content.DIALOGUE_TEXT() != null)
                return content.DIALOGUE_TEXT().GetText();

            // 改行
            if (content.DIALOGUE_NEWLINE() != null)
                return "\n";

            // エスケープシーケンス
            if (content.DIALOGUE_ESCAPE_LANGLE() != null) return "＜";
            if (content.DIALOGUE_ESCAPE_RANGLE() != null) return "＞";
            if (content.DIALOGUE_ESCAPE_LBRACKET() != null) return "「";
            if (content.DIALOGUE_ESCAPE_RBRACKET() != null) return "」";

            // セリフ内の関数呼び出し（§5.2, §5.3）
            // 変数展開 or 関数呼び出し → 実行して戻り値を文字列化
            if (content.funcCall() != null)
            {
                var result = await VisitFuncCall(content.funcCall());
                return result.ToDisplayString();
            }

            return "";
        }

        public override async Task<DaihonValue> VisitDialogueContent(DaihonParser.DialogueContentContext context)
        {
            var text = await BuildSingleDialogueContent(context);
            return DaihonValue.FromString(text);
        }
    }
}