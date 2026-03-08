// ==========================================================================
// DaihonRunner.cs
// 台本テキストのパース → インタープリタ実行を 1 メソッドにまとめるファサード。
// ==========================================================================

using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using Cysharp.Threading.Tasks;
using Daihon;
using UnityEngine;

namespace Daihon.Unity
{
    /// <summary>
    /// 各シーンのメタデータ（合図・条件などルーティングに必要な情報）の抽出結果。
    /// </summary>
    public class DaihonSceneMetadata
    {
        public string SceneName { get; set; }
        public string[] SystemEvents { get; set; } // 合図
        public bool HasCondition { get; set; } // 条件の有無
        public DaihonParser.CondExprContext ConditionContext { get; set; } // 条件式のAST（後で評価するため）
    }

    /// <summary>
    /// 台本ファイル全体のメタデータ。
    /// </summary>
    public class DaihonFileMetadata
    {
        public DaihonParser.DefaultsBlockContext DefaultsBlock { get; set; } // 初期値ブロック
        public List<DaihonSceneMetadata> Scenes { get; set; } = new List<DaihonSceneMetadata>();
    }

    /// <summary>
    /// DaihonScript を Unity 上で簡単に実行・解析するためのファサードクラス。
    /// パース → 構文木の生成 → インタープリタ実行（または 解析） を一括で行います。
    /// </summary>
    public static class DaihonRunner
    {
        /// <summary>
        /// 台本テキストをパースして実行します。
        /// </summary>
        /// <param name="scriptText">台本テキスト（.daihon ファイルの内容）</param>
        /// <param name="actionHandler">セリフ表示・関数呼び出しを処理する UniTask ベースのハンドラー</param>
        /// <param name="variableStore">変数ストア</param>
        /// <returns>実行完了を待機する UniTask</returns>
        public static async UniTask RunAsync(
            string scriptText,
            IUniTaskActionHandler actionHandler,
            IVariableStore variableStore)
        {
            if (string.IsNullOrEmpty(scriptText))
            {
                Debug.LogWarning("[DaihonRunner] 台本テキストが空です。");
                return;
            }

            // 末尾に改行がなければ付加（文法が NEWLINE を文の区切りとして要求するため）
            if (!scriptText.EndsWith("\n"))
                scriptText += "\n";

            DaihonParser.FileContext tree = ParseScript(scriptText);
            if (tree == null) return;

            // UniTask → Task アダプターを挟んでインタープリタ実行
            var adapter = new UniTaskActionAdapter(actionHandler);
            var visitor = new DaihonScriptVisitor(adapter, variableStore);

            try
            {
                await visitor.VisitFile(tree);
            }
            catch (DaihonRuntimeException ex)
            {
                Debug.LogError($"[DaihonRunner] 実行時エラー: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DaihonRunner] 予期しないエラー: {ex}");
            }
        }

        /// <summary>
        /// 指定されたシーンから実行を開始します。
        /// </summary>
        public static async UniTask RunSceneAsync(
            string scriptText,
            string startSceneName,
            IUniTaskActionHandler actionHandler,
            IVariableStore variableStore)
        {
            if (string.IsNullOrEmpty(scriptText)) return;
            if (!scriptText.EndsWith("\n")) scriptText += "\n";

            DaihonParser.FileContext tree = ParseScript(scriptText);
            if (tree == null) return;

            var adapter = new UniTaskActionAdapter(actionHandler);
            // DaihonScriptVisitorのコンストラクタ内でジャンプ回数や初期値ブロックの評価が行われるため、
            // 該当シーンのASTノードを直接Visitします。
            var visitor = new DaihonScriptVisitor(adapter, variableStore);

            try
            {
                // 初期値ブロックを実行 (システム全体としての変数はStoreにあるが、ファイル固有のものがあれば適用)
                var eventDecl = tree.eventDecl();
                if (eventDecl?.defaultsBlock() != null)
                    await visitor.VisitDefaultsBlock(eventDecl.defaultsBlock());
                
                // 指定シーンを探して実行
                var scenes = eventDecl?.scene() ?? Array.Empty<DaihonParser.SceneContext>();
                var targetScene = Array.Find(scenes, s => s.HEADER_NAME().GetText().Trim() == startSceneName);
                if (targetScene == null)
                {
                    Debug.LogError($"[DaihonRunner] シーン '{startSceneName}' が見つかりません。");
                    return;
                }

                await visitor.VisitScene(targetScene);
            }
            catch (DaihonRuntimeException ex)
            {
                Debug.LogError($"[DaihonRunner] 実行時エラー: {ex.Message}");
            }
            catch (Exception ex)
            {
                // EventEndException や SceneEndException などアセンブリ内部の制御例外はここで握りつぶし、想定内の動作とする
                if (ex.GetType().Name == "EventEndException" || ex.GetType().Name == "SceneEndException")
                {
                    // 正常終了
                }
                else
                {
                    Debug.LogError($"[DaihonRunner] 予期しないエラー: {ex}");
                }
            }
        }

        /// <summary>
        /// 抽出した条件式 (AST) を評価します。
        /// </summary>
        public static async UniTask<bool> EvaluateConditionAsync(
            DaihonParser.CondExprContext conditionContext,
            IUniTaskActionHandler actionHandler,
            IVariableStore variableStore,
            DaihonParser.DefaultsBlockContext defaultsBlockContext = null)
        {
            if (conditionContext == null) return true;

            var adapter = new UniTaskActionAdapter(actionHandler);
            var visitor = new DaihonScriptVisitor(adapter, variableStore);

            try
            {
                // 初期値ブロックがあれば先に評価する（条件式で初期値変数を参照する場合があるため）
                if (defaultsBlockContext != null)
                {
                    await visitor.VisitDefaultsBlock(defaultsBlockContext);
                }

                var result = await visitor.VisitCondExpr(conditionContext);
                return result.IsTruthy();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DaihonRunner] 条件評価エラー: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 台本ファイルからメタデータ（シーン、合図、条件のAST、初期値ブロック）を抽出します。
        /// 実行は行いません。
        /// </summary>
        public static DaihonFileMetadata ExtractMetadata(string scriptText)
        {
            var result = new DaihonFileMetadata();
            if (string.IsNullOrEmpty(scriptText)) return result;
            if (!scriptText.EndsWith("\n")) scriptText += "\n";

            DaihonParser.FileContext tree = ParseScript(scriptText);
            if (tree == null) return result;

            var eventDecl = tree.eventDecl();
            if (eventDecl == null) return result;

            // 初期値ブロックの保持
            result.DefaultsBlock = eventDecl.defaultsBlock();

            foreach (var scene in eventDecl.scene())
            {
                var meta = new DaihonSceneMetadata
                {
                    SceneName = scene.HEADER_NAME()?.GetText().Trim() ?? ""
                };

                // 合図（システムイベント）の抽出
                var signalDecl = scene.signalDecl();
                if (signalDecl != null)
                {
                    var events = new List<string>();
                    var sysEventList = signalDecl.systemEventList();
                    if (sysEventList != null)
                    {
                        foreach (var sysEvt in sysEventList.systemEvent())
                        {
                            var evtStr = sysEvt.AT_SIGN().GetText();
                            foreach (var iden in sysEvt.IDENTIFIER())
                            {
                                evtStr += "." + iden.GetText();
                            }
                            events.Add(evtStr);
                        }
                    }
                    else if (signalDecl.systemEventList()?.systemEvent() != null)
                    {
                        // signalDecl は systemEventList を持つ構造
                        foreach (var sysEvt in signalDecl.systemEventList().systemEvent())
                        {
                            var evtStr = sysEvt.AT_SIGN().GetText();
                            foreach (var iden in sysEvt.IDENTIFIER())
                            {
                                evtStr += "." + iden.GetText();
                            }
                            events.Add(evtStr);
                        }
                    }
                    meta.SystemEvents = events.ToArray();
                }
                else
                {
                    meta.SystemEvents = Array.Empty<string>();
                }

                // 条件式のASTを保持
                var conditionDecl = scene.conditionDecl();
                if (conditionDecl != null)
                {
                    meta.HasCondition = true;
                    meta.ConditionContext = conditionDecl.condExpr();
                }

                result.Scenes.Add(meta);
            }

            return result;
        }

        private static DaihonParser.FileContext ParseScript(string scriptText)
        {
            var inputStream = new AntlrInputStream(scriptText);
            var lexer = new DaihonLexer(inputStream);
            var errors = new List<string>();

            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(new UnityErrorListener(errors));

            var tokenStream = new CommonTokenStream(lexer);
            var parser = new DaihonParser(tokenStream);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new UnityErrorListener(errors));

            var tree = parser.file();

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                    Debug.LogError($"[DaihonRunner] パースエラー: {error}");
                return null;
            }

            return tree;
        }

        /// <summary>
        /// TextAsset から台本を読み込んで実行します。
        /// </summary>
        public static UniTask RunAsync(
            TextAsset scriptAsset,
            IUniTaskActionHandler actionHandler,
            IVariableStore variableStore)
        {
            if (scriptAsset == null)
            {
                Debug.LogError("[DaihonRunner] TextAsset が null です。");
                return UniTask.CompletedTask;
            }

            return RunAsync(scriptAsset.text, actionHandler, variableStore);
        }

        // ================================================================
        // ANTLR エラーリスナー（Unity Console への出力用）
        // ================================================================

        private sealed class UnityErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
        {
            private readonly List<string> _errors;

            public UnityErrorListener(List<string> errors) => _errors = errors;

            public void SyntaxError(
                System.IO.TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
                int line, int charPositionInLine, string msg, RecognitionException e)
            {
                _errors.Add($"line {line}:{charPositionInLine} {msg}");
            }

            public void SyntaxError(
                System.IO.TextWriter output, IRecognizer recognizer, int offendingSymbol,
                int line, int charPositionInLine, string msg, RecognitionException e)
            {
                _errors.Add($"line {line}:{charPositionInLine} {msg}");
            }
        }
    }
}
