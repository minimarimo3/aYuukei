using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yuukei.Runtime
{
    public sealed class SettingsWindow : MonoBehaviour
    {
        private enum SettingsPage
        {
            Appearance,
            Packages,
            Marketplace,
            Integration,
            Behavior,
            About,
        }

        private sealed class SidebarEntry
        {
            public SidebarEntry(SettingsPage page, string label)
            {
                Page = page;
                Label = label;
            }

            public SettingsPage Page { get; }
            public string Label { get; }
        }

        private static readonly SidebarEntry[] SidebarEntries =
        {
            new SidebarEntry(SettingsPage.Appearance, "外見と振る舞い"),
            new SidebarEntry(SettingsPage.Packages, "パッケージ"),
            new SidebarEntry(SettingsPage.Marketplace, "マーケットプレイス"),
            new SidebarEntry(SettingsPage.Integration, "連携"),
            new SidebarEntry(SettingsPage.Behavior, "動作設定"),
            new SidebarEntry(SettingsPage.About, "About"),
        };

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _contentHost;
        private Label _titleLabel;
        private SettingsPage _currentPage = SettingsPage.Appearance;
        private PersistenceStore _persistenceStore;
        private PackageManager _packageManager;
        private PluginLoader _pluginLoader;
        private bool _apiKeyConfigured;

        private Func<UniTask> _closeRequested;
        private Func<string, UniTask> _switchPackageRequested;
        private Func<string, UniTask> _deletePackageRequested;
        private Func<string, UniTask> _importPackageRequested;
        private Func<bool, UniTask> _setDisabledRequested;
        private Func<bool, UniTask> _setHiddenRequested;
        private Func<ShortcutConfigData, UniTask> _saveShortcutConfigRequested;
        private Func<string, string, UniTask> _saveSecretRequested;
        private Func<string, UniTask> _deleteSecretRequested;
        private Func<UniTask> _approveDllsRequested;
        private Func<UniTask> _clearDllApprovalsRequested;

        public void Initialize(
            Func<UniTask> closeRequested,
            Func<string, UniTask> switchPackageRequested,
            Func<string, UniTask> deletePackageRequested,
            Func<string, UniTask> importPackageRequested,
            Func<bool, UniTask> setDisabledRequested,
            Func<bool, UniTask> setHiddenRequested,
            Func<ShortcutConfigData, UniTask> saveShortcutConfigRequested,
            Func<string, string, UniTask> saveSecretRequested,
            Func<string, UniTask> deleteSecretRequested,
            Func<UniTask> approveDllsRequested,
            Func<UniTask> clearDllApprovalsRequested)
        {
            _closeRequested = closeRequested;
            _switchPackageRequested = switchPackageRequested;
            _deletePackageRequested = deletePackageRequested;
            _importPackageRequested = importPackageRequested;
            _setDisabledRequested = setDisabledRequested;
            _setHiddenRequested = setHiddenRequested;
            _saveShortcutConfigRequested = saveShortcutConfigRequested;
            _saveSecretRequested = saveSecretRequested;
            _deleteSecretRequested = deleteSecretRequested;
            _approveDllsRequested = approveDllsRequested;
            _clearDllApprovalsRequested = clearDllApprovalsRequested;

            if (_document != null)
            {
                return;
            }

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 780);

            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1f;
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.backgroundColor = new Color(0.96f, 0.96f, 0.93f);

            var sidebar = new VisualElement();
            sidebar.style.width = 260f;
            sidebar.style.backgroundColor = new Color(0.14f, 0.16f, 0.18f);
            sidebar.style.paddingTop = 24f;
            sidebar.style.paddingBottom = 24f;
            sidebar.style.paddingLeft = 18f;
            sidebar.style.paddingRight = 18f;

            var appName = new Label("Yuukei");
            appName.style.fontSize = 28f;
            appName.style.unityFontStyleAndWeight = FontStyle.Bold;
            appName.style.color = Color.white;
            appName.style.marginBottom = 18f;
            sidebar.Add(appName);

            foreach (var entry in SidebarEntries)
            {
                var button = new Button(() => ShowPage(entry.Page))
                {
                    text = entry.Label,
                };
                button.style.height = 42f;
                button.style.marginBottom = 8f;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                sidebar.Add(button);
            }

            var main = new VisualElement();
            main.style.flexGrow = 1f;
            main.style.paddingTop = 24f;
            main.style.paddingBottom = 24f;
            main.style.paddingLeft = 28f;
            main.style.paddingRight = 28f;
            main.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 18f;

            _titleLabel = new Label();
            _titleLabel.style.fontSize = 26f;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_titleLabel);

            var closeButton = new Button(() =>
            {
                if (_closeRequested != null)
                {
                    _closeRequested().Forget();
                }
            })
            {
                text = "閉じる",
            };
            closeButton.style.width = 120f;
            closeButton.style.height = 38f;
            header.Add(closeButton);

            _contentHost = new ScrollView();
            _contentHost.style.flexGrow = 1f;

            main.Add(header);
            main.Add(_contentHost);
            _root.Add(sidebar);
            _root.Add(main);
            _root.style.display = DisplayStyle.None;

            Rebuild();
        }

        public void SetVisible(bool visible)
        {
            if (_root != null)
            {
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void Refresh(PersistenceStore persistenceStore, PackageManager packageManager, PluginLoader pluginLoader, bool apiKeyConfigured)
        {
            _persistenceStore = persistenceStore;
            _packageManager = packageManager;
            _pluginLoader = pluginLoader;
            _apiKeyConfigured = apiKeyConfigured;
            Rebuild();
        }

        private void ShowPage(SettingsPage page)
        {
            _currentPage = page;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_contentHost == null)
            {
                return;
            }

            _contentHost.Clear();
            _titleLabel.text = SidebarEntries.First(entry => entry.Page == _currentPage).Label;

            switch (_currentPage)
            {
                case SettingsPage.Appearance:
                    BuildAppearancePage();
                    break;
                case SettingsPage.Packages:
                    BuildPackagesPage();
                    break;
                case SettingsPage.Marketplace:
                    BuildMarketplacePage();
                    break;
                case SettingsPage.Integration:
                    BuildIntegrationPage();
                    break;
                case SettingsPage.Behavior:
                    BuildBehaviorPage();
                    break;
                case SettingsPage.About:
                    BuildAboutPage();
                    break;
            }
        }

        private void BuildAppearancePage()
        {
            var overrides = _persistenceStore?.Data?.Overrides ?? new OverrideSelections();
            var activePackage = _packageManager?.ActivePackage;
            var activeContent = _packageManager?.GetResolvedActiveContent() ?? new PackageContentSelection();
            _contentHost.Add(CreateSectionHeader("差し替え"));
            _contentHost.Add(CreateInfoCard(
                "台本一覧",
                overrides.Daihon.Count == 0 ? "パッケージ準拠" : "個別指定",
                activeContent.DaihonPaths.Count == 0
                    ? "未設定"
                    : string.Join("\n", activeContent.DaihonPaths.Select((path, index) => $"{index + 1}. {Path.GetFileName(path)}"))));
            _contentHost.Add(CreateInfoRow("VRM", string.IsNullOrWhiteSpace(overrides.Character) ? "パッケージ準拠" : overrides.Character));
            _contentHost.Add(CreateInfoRow("小物", overrides.Assets.Count == 0 ? "パッケージ準拠" : $"{overrides.Assets.Count} 件の個別指定"));
            _contentHost.Add(CreateInfoRow("テクスチャ", overrides.Textures.Count == 0 ? "パッケージ準拠" : $"{overrides.Textures.Count} 件の個別指定"));
            _contentHost.Add(CreateInfoRow("ロード済みモーション一覧",
                activePackage?.Manifest.Motions != null && activePackage.Manifest.Motions.Count > 0
                    ? string.Join(", ", activePackage.Manifest.Motions.Select(System.IO.Path.GetFileNameWithoutExtension))
                    : "未設定"));
        }

        private void BuildPackagesPage()
        {
            _contentHost.Add(CreateSectionHeader("現在のパッケージ"));
            _contentHost.Add(CreateInfoRow("有効パッケージ", _packageManager?.ActivePackage?.PackageId ?? "未設定"));

            var importField = new TextField("ローカルフォルダ");
            _contentHost.Add(importField);
            _contentHost.Add(new Button(() =>
            {
                if (!string.IsNullOrWhiteSpace(importField.value))
                {
                    if (_importPackageRequested != null)
                    {
                        _importPackageRequested(importField.value).Forget();
                    }
                }
            })
            {
                text = "ローカルインポート",
            });

            if (_packageManager != null)
            {
                foreach (var package in _packageManager.InstalledPackages)
                {
                    var card = CreateCard();
                    card.Add(CreateInlineLabel($"{package.Manifest.Creator} / {package.Manifest.Version}"));
                    card.Add(CreateInlineLabel(package.PackageId));

                    var actions = new VisualElement();
                    actions.style.flexDirection = FlexDirection.Row;
                    actions.style.marginTop = 10f;

                    var useButton = new Button(() =>
                    {
                        if (_switchPackageRequested != null)
                        {
                            _switchPackageRequested(package.PackageId).Forget();
                        }
                    })
                    {
                        text = _packageManager.ActivePackage?.PackageId == package.PackageId ? "使用中" : "切り替え",
                    };
                    useButton.style.marginRight = 8f;
                    useButton.SetEnabled(_packageManager.ActivePackage?.PackageId != package.PackageId);
                    actions.Add(useButton);

                    var deleteButton = new Button(() =>
                    {
                        if (_deletePackageRequested != null)
                        {
                            _deletePackageRequested(package.PackageId).Forget();
                        }
                    })
                    {
                        text = "削除",
                    };
                    deleteButton.SetEnabled(_packageManager.ActivePackage?.PackageId != package.PackageId);
                    actions.Add(deleteButton);
                    card.Add(actions);
                    _contentHost.Add(card);
                }
            }

            _contentHost.Add(CreateSectionHeader("DLL と安全性"));
            _contentHost.Add(CreateParagraph(_pluginLoader?.BuildWarningText() ?? "DLL 情報を読み込んでいます。"));
            if (_pluginLoader != null)
            {
                foreach (var candidate in _pluginLoader.Candidates)
                {
                    var state = candidate.IsActivated
                        ? "有効化済み"
                        : candidate.IsApproved ? "承認済み (次回初期化で有効化)" : "承認待ち";
                    var value = string.IsNullOrWhiteSpace(candidate.LastError) ? state : $"{state} / {candidate.LastError}";
                    _contentHost.Add(CreateInfoRow(candidate.FileName, value));
                }
            }

            var dllActions = new VisualElement();
            dllActions.style.flexDirection = FlexDirection.Row;
            var approveButton = new Button(() =>
            {
                if (_approveDllsRequested != null)
                {
                    _approveDllsRequested().Forget();
                }
            })
            {
                text = "DLL を承認",
            };
            approveButton.style.marginRight = 8f;
            dllActions.Add(approveButton);

            dllActions.Add(new Button(() =>
            {
                if (_clearDllApprovalsRequested != null)
                {
                    _clearDllApprovalsRequested().Forget();
                }
            })
            {
                text = "承認を取り消す",
            });
            _contentHost.Add(dllActions);
        }

        private void BuildMarketplacePage()
        {
            _contentHost.Add(CreateParagraph("マーケットプレイス画面の骨格のみを用意しています。MVP では実ダウンロード処理は未実装です。"));
            _contentHost.Add(CreateInfoRow("状態", "プレースホルダ"));
        }

        private void BuildIntegrationPage()
        {
            _contentHost.Add(CreateSectionHeader("連携"));
            _contentHost.Add(CreateParagraph("API キー未設定時は該当機能のみ無効化されます。外部送信や依存の可能性をこの画面で明示します。"));
            _contentHost.Add(CreateInfoRow("同期状態", "未接続"));
            _contentHost.Add(CreateInfoRow("API キー状態", _apiKeyConfigured ? "設定済み" : "未設定"));

            var apiKeyField = new TextField("LLM API キー")
            {
                isPasswordField = true,
                value = string.Empty,
            };
            _contentHost.Add(apiKeyField);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            var saveButton = new Button(() =>
            {
                if (_saveSecretRequested != null)
                {
                    _saveSecretRequested("llm.api_key", apiKeyField.value).Forget();
                }
            })
            {
                text = "保存",
            };
            saveButton.style.marginRight = 8f;
            actions.Add(saveButton);

            actions.Add(new Button(() =>
            {
                if (_deleteSecretRequested != null)
                {
                    _deleteSecretRequested("llm.api_key").Forget();
                }
            })
            {
                text = "削除",
            });
            _contentHost.Add(actions);
        }

        private void BuildBehaviorPage()
        {
            var appState = _persistenceStore?.Data?.AppState ?? new AppStateData();
            _contentHost.Add(CreateParagraph("一時無効化は反応 / 台本実行 / 自発動作を停止します。一時非表示はキャラクター描画のみ停止します。"));

            var disabledToggle = new Toggle("一時無効化") { value = appState.IsTemporarilyDisabled };
            disabledToggle.RegisterValueChangedCallback(evt =>
            {
                if (_setDisabledRequested != null)
                {
                    _setDisabledRequested(evt.newValue).Forget();
                }
            });
            _contentHost.Add(disabledToggle);

            var hiddenToggle = new Toggle("一時非表示") { value = appState.IsTemporarilyHidden };
            hiddenToggle.RegisterValueChangedCallback(evt =>
            {
                if (_setHiddenRequested != null)
                {
                    _setHiddenRequested(evt.newValue).Forget();
                }
            });
            _contentHost.Add(hiddenToggle);

            var shortcuts = appState.ShortcutConfig ?? new ShortcutConfigData();
            var disabledField = new TextField("一時無効化ショートカット") { value = shortcuts.ToggleDisabled };
            var hiddenField = new TextField("一時非表示ショートカット") { value = shortcuts.ToggleHidden };
            var settingsField = new TextField("設定表示ショートカット") { value = shortcuts.OpenSettings };
            _contentHost.Add(disabledField);
            _contentHost.Add(hiddenField);
            _contentHost.Add(settingsField);

            _contentHost.Add(new Button(() =>
            {
                var config = new ShortcutConfigData
                {
                    ToggleDisabled = disabledField.value,
                    ToggleHidden = hiddenField.value,
                    OpenSettings = settingsField.value,
                };
                if (_saveShortcutConfigRequested != null)
                {
                    _saveShortcutConfigRequested(config).Forget();
                }
            })
            {
                text = "ショートカットを保存",
            });
        }

        private void BuildAboutPage()
        {
            _contentHost.Add(CreateInfoRow("アプリ名", "Yuukei"));
            _contentHost.Add(CreateInfoRow("バージョン", Application.version));
            _contentHost.Add(CreateInfoRow("ライセンス", "Repository local MVP build"));
            _contentHost.Add(CreateParagraph("Desktop mascot runtime MVP for Windows. Credits: Yuukei spec, Daihon runtime, UniTask, UniWindowController, Unity MCP."));
        }

        private static VisualElement CreateCard()
        {
            var card = new VisualElement();
            card.style.backgroundColor = Color.white;
            card.style.marginBottom = 12f;
            card.style.paddingLeft = 16f;
            card.style.paddingRight = 16f;
            card.style.paddingTop = 14f;
            card.style.paddingBottom = 14f;
            card.style.borderTopLeftRadius = 10f;
            card.style.borderTopRightRadius = 10f;
            card.style.borderBottomLeftRadius = 10f;
            card.style.borderBottomRightRadius = 10f;
            return card;
        }

        private static VisualElement CreateInfoRow(string label, string value)
        {
            var row = CreateCard();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;

            var left = new Label(label);
            left.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(left);

            var right = new Label(value ?? string.Empty);
            right.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(right);
            return row;
        }

        private static VisualElement CreateInfoCard(string label, string state, string bodyText)
        {
            var card = CreateCard();
            card.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8f;

            var left = new Label(label);
            left.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(left);

            var right = new Label(state ?? string.Empty);
            right.style.unityTextAlign = TextAnchor.MiddleRight;
            header.Add(right);

            var body = new Label(bodyText ?? string.Empty);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.UpperLeft;

            card.Add(header);
            card.Add(body);
            return card;
        }

        private static Label CreateSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 20f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 10f;
            label.style.marginBottom = 10f;
            return label;
        }

        private static Label CreateParagraph(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 14f;
            return label;
        }

        private static Label CreateInlineLabel(string text)
        {
            var label = new Label(text);
            label.style.marginBottom = 6f;
            return label;
        }
    }
}
