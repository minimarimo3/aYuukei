using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 選択肢オーバーレイの表示・選択・キャンセルを管理するコントローラー。
    /// ユーザーにボタン形式の選択肢を提示し、選択結果を非同期で返す。
    /// </summary>
    public sealed class ChoiceOverlayController : MonoBehaviour
    {
        private const float CardWidth = 420f;
        private const float CardPadding = 22f;
        private const float CardSpacing = 14f;
        private const float TitleHeight = 36f;
        private const float ButtonHeight = 52f;
        private const float ButtonSpacing = 10f;

        private Canvas _canvas;
        private RectTransform _panelRoot;
        private RectTransform _cardRoot;
        private RectTransform _buttonContainer;
        private LayoutElement _buttonContainerLayout;
        private UniTaskCompletionSource<string> _completionSource;
        private System.Threading.CancellationTokenRegistration _cancellationRegistration;

        public bool IsShowing => _panelRoot != null && _panelRoot.gameObject.activeSelf;

        /// <summary>選択肢オーバーレイUIの初期化。パネル・カード・ボタンコンテナを構築する。</summary>
        public void Initialize(Canvas canvas)
        {
            Debug.Log("[ChoiceOverlayController] 初期化開始");
            _canvas = canvas;
            EnsureEventSystem();

            if (_panelRoot != null)
            {
                return;
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var panelObject = new GameObject("ChoiceOverlay", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(_canvas.transform, false);
            _panelRoot = panelObject.GetComponent<RectTransform>();
            _panelRoot.anchorMin = Vector2.zero;
            _panelRoot.anchorMax = Vector2.one;
            _panelRoot.offsetMin = Vector2.zero;
            _panelRoot.offsetMax = Vector2.zero;
            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var cardObject = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            cardObject.transform.SetParent(panelObject.transform, false);
            _cardRoot = cardObject.GetComponent<RectTransform>();
            _cardRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _cardRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRoot.pivot = new Vector2(0.5f, 0.5f);
            _cardRoot.anchoredPosition = Vector2.zero;
            _cardRoot.sizeDelta = new Vector2(CardWidth, 0f);
            var cardLayout = cardObject.GetComponent<VerticalLayoutGroup>();
            cardLayout.spacing = CardSpacing;
            cardLayout.padding = new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding);
            cardLayout.childControlHeight = true;
            cardLayout.childControlWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childForceExpandWidth = true;
            cardObject.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 0.95f);

            var titleObject = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObject.transform.SetParent(cardObject.transform, false);
            var titleText = titleObject.GetComponent<Text>();
            titleText.font = font;
            titleText.fontSize = 24;
            titleText.text = "選択してください";
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            var titleLayout = titleObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = TitleHeight;

            var buttonsObject = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            buttonsObject.transform.SetParent(cardObject.transform, false);
            _buttonContainer = buttonsObject.GetComponent<RectTransform>();
            _buttonContainerLayout = buttonsObject.GetComponent<LayoutElement>();
            _buttonContainerLayout.preferredHeight = 0f;
            var buttonsLayout = buttonsObject.GetComponent<VerticalLayoutGroup>();
            buttonsLayout.spacing = ButtonSpacing;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childForceExpandHeight = false;
            buttonsLayout.childForceExpandWidth = true;

            var closeButton = CreateButton("閉じる", font, () => Complete(string.Empty));
            closeButton.transform.SetParent(cardObject.transform, false);

            HideInternal();
            Debug.Log("[ChoiceOverlayController] 初期化完了");
        }

        /// <summary>選択肢を表示し、ユーザーの選択を非同期で待機する。</summary>
        public UniTask<string> ShowChoicesAsync(IReadOnlyList<string> choices, System.Threading.CancellationToken cancellationToken)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new InvalidOperationException("Choices must contain at least one entry.");
            }

            if (_panelRoot == null || _cardRoot == null || _buttonContainer == null || _buttonContainerLayout == null)
            {
                throw new InvalidOperationException("ChoiceOverlayController must be initialized before showing choices.");
            }

            CancelCurrent();
            _completionSource = new UniTaskCompletionSource<string>();

            ClearChoiceButtons();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Button firstButton = null;
            foreach (var choice in choices)
            {
                var captured = choice;
                var button = CreateButton(captured, font, () => Complete(captured));
                button.transform.SetParent(_buttonContainer, false);
                firstButton ??= button;
            }

            PrepareLayoutForDisplay(choices.Count);
            _cancellationRegistration.Dispose();
            _cancellationRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(CancelFromRuntimeCancellation);
            _panelRoot.gameObject.SetActive(true);
            _panelRoot.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_buttonContainer);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cardRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRoot);
            Canvas.ForceUpdateCanvases();
            EventSystem.current?.SetSelectedGameObject(firstButton != null ? firstButton.gameObject : null);
            Debug.Log($"[ChoiceOverlayController] 選択肢を表示 (件数: {choices.Count})");
            return _completionSource.Task;
        }

        /// <summary>現在表示中の選択肢をキャンセルする。</summary>
        public void CancelCurrent()
        {
            if (_completionSource == null)
            {
                return;
            }

            Debug.Log("[ChoiceOverlayController] 選択肢をキャンセル");
            Complete(string.Empty);
        }

        public void CancelFromRuntimeCancellation()
        {
            if (_completionSource == null)
            {
                return;
            }

            _completionSource.TrySetCanceled();
            _completionSource = null;
            HideInternal();
        }

        private void Update()
        {
            if (_completionSource != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Complete(string.Empty);
            }
        }

        /// <summary>選択を確定し、結果を返してオーバーレイを閉じる。</summary>
        private void Complete(string value)
        {
            if (_completionSource == null)
            {
                return;
            }

            Debug.Log($"[ChoiceOverlayController] 選択確定: \"{value}\"");
            _completionSource.TrySetResult(value ?? string.Empty);
            _completionSource = null;
            HideInternal();
        }

        private void PrepareLayoutForDisplay(int choiceCount)
        {
            var buttonsHeight = choiceCount * ButtonHeight;
            if (choiceCount > 1)
            {
                buttonsHeight += (choiceCount - 1) * ButtonSpacing;
            }

            var cardHeight = (CardPadding * 2f)
                + TitleHeight
                + buttonsHeight
                + ButtonHeight
                + (CardSpacing * 2f);

            _cardRoot.localScale = Vector3.one;
            _cardRoot.anchoredPosition = Vector2.zero;
            _cardRoot.sizeDelta = new Vector2(CardWidth, cardHeight);
            _buttonContainer.localScale = Vector3.one;
            _buttonContainer.sizeDelta = Vector2.zero;
            _buttonContainerLayout.preferredHeight = buttonsHeight;
        }

        private void ClearChoiceButtons()
        {
            for (var i = _buttonContainer.childCount - 1; i >= 0; i--)
            {
                var child = _buttonContainer.GetChild(i);
                child.SetParent(null, false);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void HideInternal()
        {
            _cancellationRegistration.Dispose();
            if (_panelRoot == null)
            {
                return;
            }

            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_panelRoot))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            _panelRoot.gameObject.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var existingEventSystem = FindFirstObjectByType<EventSystem>();
            if (existingEventSystem != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(eventSystemObject);
            }
        }

        private static Button CreateButton(string label, Font font, Action onClick)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.25f, 0.39f, 1f);
            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = ButtonHeight;

            var button = buttonObject.GetComponent<Button>();
            button.interactable = true;
            button.onClick.AddListener(() => onClick?.Invoke());

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;

            return button;
        }
    }
}
