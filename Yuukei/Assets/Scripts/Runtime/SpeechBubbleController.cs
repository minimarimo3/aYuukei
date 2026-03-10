using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 吹き出し（スピーチバブル）の表示・非表示・テーマ適用を管理するコントローラー。
    /// マスコットの頭上にテキストを表示し、一定時間後に自動で非表示にする。
    /// </summary>
    public sealed class SpeechBubbleController : MonoBehaviour
    {
        private Canvas _canvas;
        private Camera _worldCamera;
        private Func<Vector3> _anchorProvider;
        private RectTransform _root;
        private Image _background;
        private Image _tail;
        private Text _label;
        private int _displayVersion;

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

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootObject = new GameObject("SpeechBubble", typeof(RectTransform), typeof(Image));
            rootObject.transform.SetParent(_canvas.transform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _background = rootObject.GetComponent<Image>();
            _background.color = new Color(0.12f, 0.15f, 0.22f, 0.96f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(0f, 0f);
            _root.sizeDelta = new Vector2(340f, 120f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(rootObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 18f);
            labelRect.offsetMax = new Vector2(-18f, -18f);
            _label = labelObject.GetComponent<Text>();
            _label.font = font;
            _label.fontSize = 22;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.color = Color.white;

            var tailObject = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            tailObject.transform.SetParent(rootObject.transform, false);
            _tail = tailObject.GetComponent<Image>();
            _tail.color = _background.color;
            var tailRect = tailObject.GetComponent<RectTransform>();
            tailRect.sizeDelta = new Vector2(24f, 24f);
            tailRect.anchorMin = new Vector2(0.5f, 0f);
            tailRect.anchorMax = new Vector2(0.5f, 0f);
            tailRect.anchoredPosition = new Vector2(0f, -10f);
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            rootObject.SetActive(false);
            Debug.Log("[SpeechBubbleController] 初期化完了");
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
            _root.gameObject.SetActive(true);
            UpdatePosition();
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
            ApplySprite(_background, backgroundTexturePath);
            ApplySprite(_tail, tailTexturePath);
        }

        private void Update()
        {
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_root == null || !_root.gameObject.activeSelf || _anchorProvider == null)
            {
                return;
            }

            var anchor = _anchorProvider.Invoke();
            var screenPoint = RectTransformUtility.WorldToScreenPoint(_worldCamera, anchor);
            _root.position = screenPoint + new Vector2(0f, 48f);
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

        private static void ApplySprite(Image targetImage, string path)
        {
            if (targetImage == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                Destroy(texture);
                return;
            }

            targetImage.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            targetImage.type = Image.Type.Sliced;
        }
    }
}
