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
    public sealed class ChoiceOverlayController : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _panelRoot;
        private RectTransform _buttonContainer;
        private UniTaskCompletionSource<string> _completionSource;
        private System.Threading.CancellationTokenRegistration _cancellationRegistration;

        public void Initialize(Canvas canvas)
        {
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
            var cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(420f, 0f);
            var cardLayout = cardObject.GetComponent<VerticalLayoutGroup>();
            cardLayout.spacing = 14f;
            cardLayout.padding = new RectOffset(22, 22, 22, 22);
            cardLayout.childControlHeight = true;
            cardLayout.childControlWidth = true;
            cardLayout.childForceExpandHeight = false;
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
            titleLayout.preferredHeight = 36f;

            var buttonsObject = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            buttonsObject.transform.SetParent(cardObject.transform, false);
            _buttonContainer = buttonsObject.GetComponent<RectTransform>();
            var buttonsLayout = buttonsObject.GetComponent<VerticalLayoutGroup>();
            buttonsLayout.spacing = 10f;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childForceExpandHeight = false;

            var closeButton = CreateButton("閉じる", font, () => Complete(string.Empty));
            closeButton.transform.SetParent(cardObject.transform, false);

            panelObject.SetActive(false);
        }

        public UniTask<string> ShowChoicesAsync(IReadOnlyList<string> choices, System.Threading.CancellationToken cancellationToken)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new InvalidOperationException("Choices must contain at least one entry.");
            }

            CancelCurrent();
            _completionSource = new UniTaskCompletionSource<string>();

            foreach (Transform child in _buttonContainer)
            {
                Destroy(child.gameObject);
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Button firstButton = null;
            foreach (var choice in choices)
            {
                var captured = choice;
                var button = CreateButton(captured, font, () => Complete(captured));
                button.transform.SetParent(_buttonContainer, false);
                firstButton ??= button;
            }

            _panelRoot.gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(firstButton != null ? firstButton.gameObject : null);

            _cancellationRegistration.Dispose();
            _cancellationRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(CancelFromRuntimeCancellation);
            return _completionSource.Task;
        }

        public void CancelCurrent()
        {
            if (_completionSource == null)
            {
                return;
            }

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
            _cancellationRegistration.Dispose();
            _panelRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_completionSource != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Complete(string.Empty);
            }
        }

        private void Complete(string value)
        {
            if (_completionSource == null)
            {
                return;
            }

            _completionSource.TrySetResult(value ?? string.Empty);
            _completionSource = null;
            _cancellationRegistration.Dispose();
            _panelRoot.gameObject.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystemObject);
        }

        private static Button CreateButton(string label, Font font, Action onClick)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.25f, 0.39f, 1f);
            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 52f;

            var button = buttonObject.GetComponent<Button>();
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

            return button;
        }
    }
}
