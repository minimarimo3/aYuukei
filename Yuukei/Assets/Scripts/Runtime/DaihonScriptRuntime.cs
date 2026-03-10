using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Antlr4.Runtime;
using Cysharp.Threading.Tasks;
using Daihon;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 台本スクリプトのパース・実行エンジン。
    /// スクリプトテキストを解析し、イベントに応じたシーンを選択・実行する。
    /// </summary>
    public sealed class DaihonScriptRuntime
    {
        public sealed class SceneMetadata
        {
            public string SceneName;
            public string[] RawSystemEvents;
            public bool HasCondition;
            public DaihonParser.CondExprContext ConditionContext;
            public DaihonParser.SceneContext SceneContext;
        }

        public sealed class ScriptMetadata
        {
            public string ScriptText;
            public DaihonParser.FileContext FileContext;
            public DaihonParser.DefaultsBlockContext DefaultsBlock;
            public DaihonParser.PreconditionBlockContext PreconditionBlock;
            public List<SceneMetadata> Scenes = new List<SceneMetadata>();
            public Dictionary<string, DaihonParser.SceneContext> SceneLookup = new Dictionary<string, DaihonParser.SceneContext>(StringComparer.Ordinal);
        }

        /// <summary>スクリプトテキストを解析し、メタデータを返す。</summary>
        public ScriptMetadata Parse(string scriptText)
        {
            UnityEngine.Debug.Log("[DaihonScriptRuntime] パース開始");
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                UnityEngine.Debug.Log("[DaihonScriptRuntime] 空のスクリプト — パース中断");
                return null;
            }

            if (!scriptText.EndsWith("\n", StringComparison.Ordinal))
            {
                scriptText += "\n";
            }

            var errors = new List<string>();
            var inputStream = new AntlrInputStream(scriptText);
            var lexer = new DaihonLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(new RuntimeErrorListener(errors));

            var tokenStream = new CommonTokenStream(lexer);
            var parser = new DaihonParser(tokenStream);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new RuntimeErrorListener(errors));

            var tree = parser.file();
            if (errors.Count > 0)
            {
                UnityEngine.Debug.LogError($"[DaihonScriptRuntime] パース失敗 — {errors.Count} 件のエラー");
                foreach (var error in errors)
                {
                    UnityEngine.Debug.LogError($"[DaihonScriptRuntime] Parse error: {error}");
                }

                return null;
            }

            var metadata = new ScriptMetadata
            {
                ScriptText = scriptText,
                FileContext = tree,
            };

            var eventDecl = tree.eventDecl();
            if (eventDecl == null)
            {
                return metadata;
            }

            metadata.DefaultsBlock = eventDecl.defaultsBlock();
            metadata.PreconditionBlock = eventDecl.preconditionBlock();

            foreach (var scene in eventDecl.scene())
            {
                var sceneName = scene.HEADER_NAME()?.GetText().Trim() ?? string.Empty;
                var rawEvents = scene.signalDecl()?.systemEventList()?.systemEvent()
                    .Select(systemEvent => systemEvent.GetText())
                    .ToArray() ?? Array.Empty<string>();

                var entry = new SceneMetadata
                {
                    SceneName = sceneName,
                    RawSystemEvents = rawEvents,
                    HasCondition = scene.conditionDecl() != null,
                    ConditionContext = scene.conditionDecl()?.condExpr(),
                    SceneContext = scene,
                };

                metadata.Scenes.Add(entry);
                metadata.SceneLookup[sceneName] = scene;
            }

            UnityEngine.Debug.Log($"[DaihonScriptRuntime] パース成功 — シーン数: {metadata.Scenes.Count}");
            return metadata;
        }

        /// <summary>指定イベントに一致するシーンを探して実行する。</summary>
        public async UniTask RunEventAsync(
            ScriptMetadata metadata,
            string canonicalEventName,
            AliasRegistry aliasRegistry,
            IActionHandler actionHandler,
            YuukeiVariableStore variableStore,
            CancellationToken cancellationToken)
        {
            if (metadata == null)
            {
                return;
            }

            UnityEngine.Debug.Log($"[DaihonScriptRuntime] イベント '{canonicalEventName}' の実行開始（シーン数: {metadata.Scenes.Count}）");
            var visitor = new DaihonScriptVisitor(actionHandler, variableStore);

            try
            {
                if (metadata.DefaultsBlock != null)
                {
                    await visitor.VisitDefaultsBlock(metadata.DefaultsBlock);
                }

                if (metadata.PreconditionBlock != null)
                {
                    await visitor.VisitPreconditionBlock(metadata.PreconditionBlock);
                }

                var defaultScenes = new List<SceneMetadata>();
                var executedSpecificScene = false;

                foreach (var scene in metadata.Scenes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scene.RawSystemEvents.Length > 0)
                    {
                        if (!SceneMatches(scene, canonicalEventName, aliasRegistry))
                        {
                            continue;
                        }

                        UnityEngine.Debug.Log($"[DaihonScriptRuntime] シーン '{scene.SceneName}' がイベント '{canonicalEventName}' に一致");

                        if (scene.HasCondition && !await EvaluateConditionAsync(scene.ConditionContext, actionHandler, variableStore, metadata.DefaultsBlock))
                        {
                            UnityEngine.Debug.Log($"[DaihonScriptRuntime] シーン '{scene.SceneName}' の条件が不成立 — スキップ");
                            continue;
                        }

                        executedSpecificScene = true;
                        var jumped = await ExecuteSceneWithFlowAsync(visitor, scene.SceneContext, metadata.SceneLookup, cancellationToken);
                        if (jumped)
                        {
                            UnityEngine.Debug.Log($"[DaihonScriptRuntime] シーン '{scene.SceneName}' でジャンプ発生 — イベント処理終了");
                            return;
                        }
                    }
                    else if (scene.HasCondition)
                    {
                        if (!await EvaluateConditionAsync(scene.ConditionContext, actionHandler, variableStore, metadata.DefaultsBlock))
                        {
                            continue;
                        }

                        executedSpecificScene = true;
                        var jumped = await ExecuteSceneWithFlowAsync(visitor, scene.SceneContext, metadata.SceneLookup, cancellationToken);
                        if (jumped)
                        {
                            return;
                        }
                    }
                    else
                    {
                        defaultScenes.Add(scene);
                    }
                }

                if (!executedSpecificScene && defaultScenes.Count > 0)
                {
                    UnityEngine.Debug.Log($"[DaihonScriptRuntime] 特定シーン未実行 — デフォルトシーンから1つ選択（候補: {defaultScenes.Count}）");
                    var chosen = defaultScenes[UnityEngine.Random.Range(0, defaultScenes.Count)];
                    await ExecuteSceneWithFlowAsync(visitor, chosen.SceneContext, metadata.SceneLookup, cancellationToken);
                }
            }
            catch (Exception exception) when (IsControlException(exception, "EventEndException"))
            {
            }
        }

        /// <summary>条件式を評価し、真偽を返す。</summary>
        public async UniTask<bool> EvaluateConditionAsync(
            DaihonParser.CondExprContext conditionContext,
            IActionHandler actionHandler,
            YuukeiVariableStore variableStore,
            DaihonParser.DefaultsBlockContext defaultsBlock)
        {
            if (conditionContext == null)
            {
                return true;
            }

            var visitor = new DaihonScriptVisitor(actionHandler, variableStore);
            if (defaultsBlock != null)
            {
                await visitor.VisitDefaultsBlock(defaultsBlock);
            }

            var result = await visitor.VisitCondExpr(conditionContext);
            var isTruthy = result.IsTruthy();
            UnityEngine.Debug.Log($"[DaihonScriptRuntime] 条件評価結果: {isTruthy}");
            return isTruthy;
        }

        private static bool SceneMatches(SceneMetadata scene, string canonicalEventName, AliasRegistry aliasRegistry)
        {
            foreach (var rawEvent in scene.RawSystemEvents)
            {
                var trimmed = rawEvent.Trim().TrimStart('＠', '@');
                if (aliasRegistry.TryResolveEventName(trimmed, out var canonical)
                    && string.Equals(canonical, canonicalEventName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>シーンを実行し、ジャンプがあれば追跡する。</summary>
        private static async UniTask<bool> ExecuteSceneWithFlowAsync(
            DaihonScriptVisitor visitor,
            DaihonParser.SceneContext sceneContext,
            IReadOnlyDictionary<string, DaihonParser.SceneContext> sceneLookup,
            CancellationToken cancellationToken)
        {
            var jumped = false;
            var current = sceneContext;
            var jumpCount = 0;

            while (current != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await visitor.VisitScene(current);
                    return jumped;
                }
                catch (Exception exception) when (IsControlException(exception, "SceneEndException"))
                {
                    return jumped;
                }
                catch (Exception exception) when (IsControlException(exception, "SceneJumpException"))
                {
                    jumped = true;
                    jumpCount++;
                    var targetSceneName = GetJumpTargetSceneName(exception);
                    UnityEngine.Debug.Log($"[DaihonScriptRuntime] シーンジャンプ #{jumpCount}: → '{targetSceneName}'");
                    if (jumpCount > 1000)
                    {
                        throw new DaihonRuntimeException("ジャンプ回数が上限の 1000 回を超えました。");
                    }

                    if (!sceneLookup.TryGetValue(targetSceneName, out current))
                    {
                        throw new DaihonRuntimeException($"ジャンプ先のシーン '{targetSceneName}' が存在しません。");
                    }
                }
            }

            return jumped;
        }

        private static bool IsControlException(Exception exception, string expectedName)
        {
            return string.Equals(exception.GetType().Name, expectedName, StringComparison.Ordinal);
        }

        private static string GetJumpTargetSceneName(Exception exception)
        {
            var property = exception.GetType().GetProperty("TargetSceneName", BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(exception) as string ?? string.Empty;
        }

        private sealed class RuntimeErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
        {
            private readonly List<string> _errors;

            public RuntimeErrorListener(List<string> errors)
            {
                _errors = errors;
            }

            public void SyntaxError(System.IO.TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException exception)
            {
                _errors.Add($"line {line}:{charPositionInLine} {msg}");
            }

            public void SyntaxError(System.IO.TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException exception)
            {
                _errors.Add($"line {line}:{charPositionInLine} {msg}");
            }
        }
    }
}
