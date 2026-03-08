using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class WindowsDesktopAdapterTests
    {
        [Test]
        public void ApplyShortcuts_UsesInjectedHostStatuses_AndRejectsInvalidBindings()
        {
            var host = new FakeWindowsShellHost();
            var adapter = new WindowsDesktopAdapter(host);
            adapter.Initialize();

            adapter.ApplyShortcuts(new ShortcutConfigData
            {
                OpenSettings = "Ctrl+Alt+;",
                ToggleDisabled = "Ctrl+A",
                ToggleHidden = "Ctrl+InvalidKey",
            });

            var statuses = adapter.GetShortcutStatuses();

            Assert.That(host.Initialized, Is.True);
            Assert.That(statuses[ShortcutAction.OpenSettings].IsRegistered, Is.True);
            Assert.That(statuses[ShortcutAction.OpenSettings].Message, Is.EqualTo("fake native host"));
            Assert.That(statuses[ShortcutAction.ToggleDisabled].IsRegistered, Is.True);
            Assert.That(statuses[ShortcutAction.ToggleHidden].IsRegistered, Is.False);
            Assert.That(statuses[ShortcutAction.ToggleHidden].Message, Is.EqualTo("無効なショートカットです"));
        }

        [Test]
        public void Tick_ForwardsQueuedTrayAndShortcutEvents_AndReceivesShellState()
        {
            var host = new FakeWindowsShellHost();
            var adapter = new WindowsDesktopAdapter(host);
            TrayCommand? trayCommand = null;
            ShortcutAction? shortcutAction = null;
            adapter.TrayCommandRequested += command => trayCommand = command;
            adapter.ShortcutTriggered += action => shortcutAction = action;
            adapter.Initialize();

            adapter.UpdateShellState(new AppShellState(isSettingsVisible: true, isTemporarilyDisabled: true, isTemporarilyHidden: false));
            host.QueueTrayCommand(TrayCommand.Exit);
            host.QueueShortcut(ShortcutAction.OpenSettings);

            adapter.Tick();

            Assert.That(host.LastShellState.IsSettingsVisible, Is.True);
            Assert.That(host.LastShellState.IsTemporarilyDisabled, Is.True);
            Assert.That(host.LastShellState.IsTemporarilyHidden, Is.False);
            Assert.That(trayCommand, Is.EqualTo(TrayCommand.Exit));
            Assert.That(shortcutAction, Is.EqualTo(ShortcutAction.OpenSettings));
        }

        private sealed class FakeWindowsShellHost : IWindowsShellHost
        {
            private readonly Queue<TrayCommand> _trayCommands = new Queue<TrayCommand>();
            private readonly Queue<ShortcutAction> _shortcutActions = new Queue<ShortcutAction>();

            public event Action<TrayCommand> TrayCommandRequested;
            public event Action<ShortcutAction> ShortcutTriggered;

            public bool SupportsGlobalHotkeys => true;
            public bool Initialized { get; private set; }
            public AppShellState LastShellState { get; private set; }

            public void Initialize()
            {
                Initialized = true;
            }

            public void Shutdown()
            {
                Initialized = false;
            }

            public void Tick()
            {
                while (_trayCommands.Count > 0)
                {
                    TrayCommandRequested?.Invoke(_trayCommands.Dequeue());
                }

                while (_shortcutActions.Count > 0)
                {
                    ShortcutTriggered?.Invoke(_shortcutActions.Dequeue());
                }
            }

            public void ApplyShellState(AppShellState state)
            {
                LastShellState = state;
            }

            public IReadOnlyDictionary<ShortcutAction, ShortcutRegistrationStatus> ApplyShortcuts(IReadOnlyDictionary<ShortcutAction, ShortcutBinding> shortcuts)
            {
                var result = new Dictionary<ShortcutAction, ShortcutRegistrationStatus>();
                foreach (var pair in shortcuts)
                {
                    result[pair.Key] = new ShortcutRegistrationStatus(pair.Value.OriginalText, true, "fake native host");
                }

                return result;
            }

            public void QueueTrayCommand(TrayCommand command)
            {
                _trayCommands.Enqueue(command);
            }

            public void QueueShortcut(ShortcutAction action)
            {
                _shortcutActions.Enqueue(action);
            }
        }
    }
}
