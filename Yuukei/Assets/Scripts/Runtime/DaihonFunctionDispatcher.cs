using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Daihon;

namespace Yuukei.Runtime
{
    public sealed class DaihonFunctionDispatcher
    {
        private readonly AliasRegistry _aliasRegistry;
        private readonly Dictionary<string, CanonicalFunctionDelegate> _functions = new Dictionary<string, CanonicalFunctionDelegate>(StringComparer.Ordinal);
        private readonly SpeechBubbleController _speechBubbleController;
        private readonly ChoiceOverlayController _choiceOverlayController;
        private readonly MascotRuntime _mascotRuntime;
        private readonly YuukeiVariableStore _variableStore;

        public DaihonFunctionDispatcher(
            AliasRegistry aliasRegistry,
            SpeechBubbleController speechBubbleController,
            ChoiceOverlayController choiceOverlayController,
            MascotRuntime mascotRuntime,
            YuukeiVariableStore variableStore)
        {
            _aliasRegistry = aliasRegistry;
            _speechBubbleController = speechBubbleController;
            _choiceOverlayController = choiceOverlayController;
            _mascotRuntime = mascotRuntime;
            _variableStore = variableStore;
            RegisterBuiltins();
        }

        public void RegisterFunction(string canonicalName, CanonicalFunctionDelegate function)
        {
            _functions[canonicalName] = function;
        }

        public async UniTask<DaihonValue> InvokeAsync(string rawFunctionName, IReadOnlyList<DaihonValue> positionalArgs, CancellationToken cancellationToken)
        {
            if (!_aliasRegistry.TryResolveFunctionName(rawFunctionName, out var canonicalName))
            {
                throw new DaihonRuntimeException($"未定義の関数 '{rawFunctionName}' が呼び出されました。");
            }

            if (!_functions.TryGetValue(canonicalName, out var function))
            {
                throw new DaihonRuntimeException($"関数 '{canonicalName}' は登録されていません。");
            }

            var args = new DaihonValue[positionalArgs.Count];
            for (var i = 0; i < positionalArgs.Count; i++)
            {
                args[i] = positionalArgs[i];
            }

            var result = await function(args, cancellationToken);
            return result ?? DaihonValue.None;
        }

        private void RegisterBuiltins()
        {
            RegisterFunction("show_dialog", ShowDialogAsync);
            RegisterFunction("set_expression", SetExpressionAsync);
            RegisterFunction("play_motion", PlayMotionAsync);
            RegisterFunction("set_prop_visible", SetPropVisibleAsync);
            RegisterFunction("show_choices", ShowChoicesAsync);
            RegisterFunction("set_persistent", SetPersistentAsync);
        }

        private UniTask<DaihonValue?> ShowDialogAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length != 1 || args[0].Type != DaihonValue.ValueType.String)
            {
                throw new DaihonRuntimeException("show_dialog(text) は文字列 1 つだけを受け取ります。");
            }

            _speechBubbleController.ShowImmediate(args[0].AsString());
            return UniTask.FromResult<DaihonValue?>(DaihonValue.None);
        }

        private UniTask<DaihonValue?> SetExpressionAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length != 1 || args[0].Type != DaihonValue.ValueType.String)
            {
                throw new DaihonRuntimeException("set_expression(name) は文字列 1 つだけを受け取ります。");
            }

            _mascotRuntime.SetExpression(args[0].AsString());
            return UniTask.FromResult<DaihonValue?>(DaihonValue.None);
        }

        private UniTask<DaihonValue?> PlayMotionAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length != 1 || args[0].Type != DaihonValue.ValueType.String)
            {
                throw new DaihonRuntimeException("play_motion(name) は文字列 1 つだけを受け取ります。");
            }

            _mascotRuntime.PlayMotion(args[0].AsString());
            return UniTask.FromResult<DaihonValue?>(DaihonValue.None);
        }

        private UniTask<DaihonValue?> SetPropVisibleAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length != 2 || args[0].Type != DaihonValue.ValueType.String || args[1].Type != DaihonValue.ValueType.Boolean)
            {
                throw new DaihonRuntimeException("set_prop_visible(name, visible) は文字列と真偽値を受け取ります。");
            }

            _mascotRuntime.SetPropVisible(args[0].AsString(), args[1].AsBoolean());
            return UniTask.FromResult<DaihonValue?>(DaihonValue.None);
        }

        private async UniTask<DaihonValue?> ShowChoicesAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length == 0)
            {
                throw new DaihonRuntimeException("show_choices(...) は 1 個以上の文字列引数が必要です。");
            }

            var choices = new List<string>();
            foreach (var arg in args)
            {
                if (arg.Type != DaihonValue.ValueType.String)
                {
                    throw new DaihonRuntimeException("show_choices(...) の引数はすべて文字列である必要があります。");
                }

                choices.Add(arg.AsString());
            }

            var selected = await _choiceOverlayController.ShowChoicesAsync(choices, cancellationToken);
            return DaihonValue.FromString(selected);
        }

        private UniTask<DaihonValue?> SetPersistentAsync(DaihonValue[] args, CancellationToken cancellationToken)
        {
            if (args.Length != 2 || args[0].Type != DaihonValue.ValueType.String)
            {
                throw new DaihonRuntimeException("set_persistent(key, value) はキー文字列と値を受け取ります。");
            }

            if (args[1].Type != DaihonValue.ValueType.Boolean
                && args[1].Type != DaihonValue.ValueType.Number
                && args[1].Type != DaihonValue.ValueType.String)
            {
                throw new DaihonRuntimeException("set_persistent(key, value) の value は bool / number / string のみです。");
            }

            _variableStore.SetValue(args[0].AsString(), args[1]);
            return UniTask.FromResult<DaihonValue?>(DaihonValue.None);
        }
    }
}
