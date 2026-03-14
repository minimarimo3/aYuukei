using System;
using System.IO;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yuukei.Runtime
{
    [Serializable]
    public sealed class SpeechBubbleTextStyle
    {
        public float FontSize = 6f;
        public Color TextColor = Color.black;

        public SpeechBubbleTextStyle Clone()
        {
            return new SpeechBubbleTextStyle
            {
                FontSize = FontSize,
                TextColor = TextColor,
            };
        }

        public void Normalize()
        {
            FontSize = Mathf.Max(1f, FontSize);
        }

        public static SpeechBubbleTextStyle CreateDefault()
        {
            return new SpeechBubbleTextStyle();
        }
    }

    /// <summary>
    /// 吹き出し（スピーチバブル）の表示・非表示・テーマ適用を管理するコントローラー。
    /// マスコットの頭上にテキストを表示し、一定時間後に自動で非表示にする。
    /// </summary>
    public sealed class SpeechBubbleController : MonoBehaviour
    {
        private const float HorizontalPadding = 4f + 1f;
        private const float VerticalPadding = 3f + 1f;
        private const float MinimumBodyWidth = 40f + 20f;
        private const float MaximumViewportWidthFraction = 0.25f;
        private const float MinimumTailHeight = 4f;
        private const float MaximumTailHeight = 6f;
        private const float TailHeightRatio = 0.18f;
        private const float TailAttachmentInset = 12f;
        private const float ScreenSafeInset = 12f;
        private const float TailTipClearance = 8f;
        private static readonly Color DefaultBubbleTint = new Color(0.12f, 0.15f, 0.22f, 0.96f);

        private sealed class RuntimeSpriteHandle
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private Canvas _canvas;
        private Camera _worldCamera;
        private Func<Vector3> _anchorProvider;
        private RectTransform _root;
        private RectTransform _body;
        private RectTransform _backgroundRect;
        private RectTransform _labelRect;
        private RectTransform _tailRect;
        private Image _background;
        private Image _tail;
        private TextMeshProUGUI _label;
        private int _displayVersion;
        private SpeechBubbleTextStyle _textStyle = SpeechBubbleTextStyle.CreateDefault();
        private readonly RuntimeSpriteHandle _backgroundTheme = new RuntimeSpriteHandle();
        private readonly RuntimeSpriteHandle _tailTheme = new RuntimeSpriteHandle();

        /// <summary>吹き出しUIの初期化。Canvas上にルート・ラベル・テールを構築する。</summary>
        public void Initialize(Canvas canvas, Camera worldCamera, Func<Vector3> anchorProvider)
        {
            Debug.Log("[SpeechBubbleController] 初期化開始");
            _canvas = canvas;
            _worldCamera = worldCamera;
            _anchorProvider = anchorProvider;

            if (_root != null)
            {
                return;
            }

            var rootObject = new GameObject("SpeechBubble", typeof(RectTransform));
            rootObject.transform.SetParent(_canvas.transform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.sizeDelta = Vector2.zero;

            var bodyObject = new GameObject("Body", typeof(RectTransform), typeof(RectMask2D));
            bodyObject.transform.SetParent(rootObject.transform, false);
            _body = bodyObject.GetComponent<RectTransform>();
            _body.anchorMin = new Vector2(0.5f, 0f);
            _body.anchorMax = new Vector2(0.5f, 0f);
            _body.pivot = new Vector2(0.5f, 0f);
            _body.anchoredPosition = Vector2.zero;
            _body.sizeDelta = new Vector2(MinimumBodyWidth, 56f);
            var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(bodyObject.transform, false);
            _backgroundRect = backgroundObject.GetComponent<RectTransform>();
            _backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            _backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            _backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            _backgroundRect.anchoredPosition = Vector2.zero;
            _backgroundRect.sizeDelta = _body.sizeDelta;
            _background = backgroundObject.GetComponent<Image>();
            _background.color = DefaultBubbleTint;
            _background.type = Image.Type.Simple;
            _background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(bodyObject.transform, false);
            _labelRect = labelObject.GetComponent<RectTransform>();
            _labelRect.anchorMin = Vector2.zero;
            _labelRect.anchorMax = Vector2.one;
            _labelRect.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
            _labelRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);
            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.raycastTarget = false;
            ApplyCurrentTextStyle();

            var tailObject = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            tailObject.transform.SetParent(rootObject.transform, false);
            _tail = tailObject.GetComponent<Image>();
            _tailRect = tailObject.GetComponent<RectTransform>();
            _tailRect.anchorMin = new Vector2(0.5f, 0f);
            _tailRect.anchorMax = new Vector2(0.5f, 0f);
            _tailRect.pivot = new Vector2(0.5f, 1f);
            _tailRect.anchoredPosition = Vector2.zero;
            _tailRect.localRotation = Quaternion.identity;
            _tailRect.sizeDelta = new Vector2(MinimumTailHeight, MinimumTailHeight);
            _tail.type = Image.Type.Simple;
            _tail.preserveAspect = true;
            _tail.raycastTarget = false;
            _tail.enabled = false;

            rootObject.SetActive(false);
            Debug.Log("[SpeechBubbleController] 初期化完了");
        }

        /// <summary>吹き出しテキストの見た目を外部から更新する。settings/package 側の将来導線用。</summary>
        public void ApplyTextStyle(SpeechBubbleTextStyle style)
        {
            _textStyle = style?.Clone() ?? SpeechBubbleTextStyle.CreateDefault();
            _textStyle.Normalize();
            ApplyCurrentTextStyle();
            RefreshBubbleLayout();
            UpdateBubblePosition();
        }

        /// <summary>現在の吹き出しテキストスタイルを取得する。</summary>
        public SpeechBubbleTextStyle GetTextStyle()
        {
            return _textStyle.Clone();
        }

        /// <summary>テキストを表示し、文字数に応じた時間だけ待機してから自動で非表示にする。</summary>
        public async UniTask ShowDialogueAsync(string text, System.Threading.CancellationToken cancellationToken)
        {
            ShowImmediate(text);
            var version = _displayVersion;
            var duration = Mathf.Clamp(text.Length * 0.08f, 1.5f, 4.5f);
            Debug.Log($"[SpeechBubbleController] ダイアログ表示: \"{text}\" (表示時間: {duration:F1}秒)");
            await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: cancellationToken);
            if (version == _displayVersion)
            {
                Hide();
            }
        }

        /// <summary>テキストを即座に表示する。自動非表示タイマーも開始する。</summary>
        public void ShowImmediate(string text, float autoHideSeconds = 2.8f)
        {
            if (_root == null)
            {
                return;
            }

            _displayVersion++;
            _label.text = text ?? string.Empty;
            RefreshBubbleLayout();
            _root.gameObject.SetActive(true);
            UpdateBubblePosition();
            Debug.Log($"[SpeechBubbleController] 即時表示: \"{text}\"");
            ScheduleAutoHideAsync(_displayVersion, autoHideSeconds).Forget();
        }

        /// <summary>吹き出しを非表示にする。</summary>
        public void Hide()
        {
            Debug.Log("[SpeechBubbleController] 吹き出しを非表示");
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>吹き出しのテーマ（背景・テール画像）を適用する。</summary>
        public void ApplyTheme(string backgroundTexturePath, string tailTexturePath)
        {
            Debug.Log($"[SpeechBubbleController] テーマ適用 背景: {backgroundTexturePath}, テール: {tailTexturePath}");
            ApplyBackgroundTheme(backgroundTexturePath);
            ApplyTailTheme(tailTexturePath);
            RefreshBubbleLayout();
            UpdateBubblePosition();
        }

        private void LateUpdate()
        {
            UpdateBubblePosition();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeSprite(_backgroundTheme);
            ReleaseRuntimeSprite(_tailTheme);
        }

        private void RefreshBubbleLayout()
        {
            if (_canvas == null || _body == null || _label == null)
            {
                return;
            }

            var canvasRect = _canvas.transform as RectTransform;
            var canvasWidth = canvasRect != null && canvasRect.rect.width > 0f
                ? canvasRect.rect.width
                : Screen.width;
            var maxBodyWidth = Mathf.Max(MinimumBodyWidth, canvasWidth * MaximumViewportWidthFraction);
            var maxLabelWidth = Mathf.Max(1f, maxBodyWidth - (HorizontalPadding * 2f));

            _label.ForceMeshUpdate();
            var initialPreferred = _label.GetPreferredValues(_label.text, maxLabelWidth, 0f);
            var bodyWidth = Mathf.Clamp(Mathf.Ceil(initialPreferred.x + (HorizontalPadding * 2f)), MinimumBodyWidth, maxBodyWidth);
            var finalLabelWidth = Mathf.Max(1f, bodyWidth - (HorizontalPadding * 2f));
            var wrappedPreferred = _label.GetPreferredValues(_label.text, finalLabelWidth, 0f);
            var minimumHeight = Mathf.Ceil(_label.fontSize + (VerticalPadding * 2f));
            var bodyHeight = Mathf.Max(minimumHeight, Mathf.Ceil(wrappedPreferred.y + (VerticalPadding * 2f)));

            _body.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            _labelRect.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
            _labelRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);
            RefreshBackgroundLayout(bodyWidth, bodyHeight);
            RefreshTailLayout(bodyHeight);
        }

        private void RefreshBackgroundLayout(float bodyWidth, float bodyHeight)
        {
            if (_backgroundRect == null)
            {
                return;
            }

            if (_background.sprite == null)
            {
                _backgroundRect.sizeDelta = new Vector2(bodyWidth, bodyHeight);
                return;
            }

            var spriteRect = _background.sprite.rect;
            var spriteAspect = spriteRect.height > 0f
                ? spriteRect.width / spriteRect.height
                : 1f;
            var bodyAspect = bodyHeight > 0f ? bodyWidth / bodyHeight : spriteAspect;

            if (bodyAspect >= spriteAspect)
            {
                _backgroundRect.sizeDelta = new Vector2(bodyWidth, bodyWidth / spriteAspect);
            }
            else
            {
                _backgroundRect.sizeDelta = new Vector2(bodyHeight * spriteAspect, bodyHeight);
            }
        }

        private void RefreshTailLayout(float bodyHeight)
        {
            if (_tailRect == null || !_tail.enabled || _tail.sprite == null)
            {
                return;
            }

            var tailHeight = Mathf.Clamp(bodyHeight * TailHeightRatio, MinimumTailHeight, MaximumTailHeight);
            var spriteRect = _tail.sprite.rect;
            var aspect = spriteRect.height > 0f
                ? spriteRect.width / spriteRect.height
                : 1f;
            _tailRect.sizeDelta = new Vector2(tailHeight * aspect, tailHeight);
        }

        private void UpdateBubblePosition()
        {
            if (_root == null || !_root.gameObject.activeSelf || _anchorProvider == null)
            {
                return;
            }

            var canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
            if (canvasRect == null)
            {
                return;
            }

            var anchor = _anchorProvider.Invoke();
            var screenPoint = RectTransformUtility.WorldToScreenPoint(_worldCamera, anchor);
            var uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, uiCamera, out var anchorLocalPoint))
            {
                return;
            }

            var tailHeight = _tail.enabled ? _tailRect.sizeDelta.y : 0f;
            var desiredRootPosition = new Vector2(anchorLocalPoint.x, anchorLocalPoint.y + tailHeight + TailTipClearance);
            var safeRect = canvasRect.rect;
            safeRect.xMin += ScreenSafeInset;
            safeRect.xMax -= ScreenSafeInset;
            safeRect.yMin += ScreenSafeInset;
            safeRect.yMax -= ScreenSafeInset;

            var bodyHalfWidth = _body.sizeDelta.x * 0.5f;
            var clampedX = Mathf.Clamp(desiredRootPosition.x, safeRect.xMin + bodyHalfWidth, safeRect.xMax - bodyHalfWidth);
            var clampedY = Mathf.Clamp(desiredRootPosition.y, safeRect.yMin, safeRect.yMax - _body.sizeDelta.y);

            _root.anchoredPosition = new Vector2(clampedX, clampedY);
            UpdateTailPosition(anchorLocalPoint.x - clampedX);
        }

        private async UniTaskVoid ScheduleAutoHideAsync(int version, float autoHideSeconds)
        {
            if (autoHideSeconds <= 0f)
            {
                return;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(autoHideSeconds));
            if (version == _displayVersion)
            {
                Hide();
            }
        }

        private void UpdateTailPosition(float anchorOffsetFromBodyCenter)
        {
            if (_tailRect == null || !_tail.enabled)
            {
                return;
            }

            // Tail sprites are authored in their final orientation:
            // the sprite's top edge is the root edge that attaches to the bubble body.
            var availableHalfWidth = Mathf.Max(0f, (_body.sizeDelta.x * 0.5f) - (_tailRect.sizeDelta.x * 0.5f) - TailAttachmentInset);
            var tailX = Mathf.Clamp(anchorOffsetFromBodyCenter, -availableHalfWidth, availableHalfWidth);
            _tailRect.anchoredPosition = new Vector2(tailX, 0f);
        }

        private void ApplyBackgroundTheme(string backgroundTexturePath)
        {
            if (TryLoadRuntimeSprite(backgroundTexturePath, _backgroundTheme, new Vector2(0.5f, 0.5f), out var sprite))
            {
                _background.sprite = sprite;
                _background.type = Image.Type.Simple;
                _background.preserveAspect = true;
                _background.color = Color.white;
                return;
            }

            _background.sprite = null;
            _background.type = Image.Type.Simple;
            _background.preserveAspect = false;
            _background.color = DefaultBubbleTint;
        }

        private void ApplyTailTheme(string tailTexturePath)
        {
            if (TryLoadRuntimeSprite(tailTexturePath, _tailTheme, new Vector2(0.5f, 1f), out var sprite))
            {
                _tail.sprite = sprite;
                _tail.type = Image.Type.Simple;
                _tail.preserveAspect = true;
                _tail.color = Color.white;
                _tail.enabled = true;
                return;
            }

            _tail.sprite = null;
            _tail.enabled = false;
        }

        private static bool TryLoadRuntimeSprite(string path, RuntimeSpriteHandle handle, Vector2 pivot, out Sprite sprite)
        {
            sprite = null;
            ReleaseRuntimeSprite(handle);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                Destroy(texture);
                return false;
            }

            handle.Texture = texture;
            handle.Sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), pivot);
            sprite = handle.Sprite;
            return true;
        }

        private void ApplyCurrentTextStyle()
        {
            _textStyle ??= SpeechBubbleTextStyle.CreateDefault();
            _textStyle.Normalize();
            if (_label == null)
            {
                return;
            }

            _label.fontSize = _textStyle.FontSize;
            _label.color = _textStyle.TextColor;
        }

        private static void ReleaseRuntimeSprite(RuntimeSpriteHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            if (handle.Sprite != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(handle.Sprite);
                }
                else
                {
                    DestroyImmediate(handle.Sprite);
                }

                handle.Sprite = null;
            }

            if (handle.Texture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(handle.Texture);
                }
                else
                {
                    DestroyImmediate(handle.Texture);
                }

                handle.Texture = null;
            }
        }
    }
}
