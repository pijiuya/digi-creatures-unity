using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigiCreatures
{
    public sealed class CreatureSubtitleHud : MonoBehaviour
    {
        private const string HudName = "智能体字幕";
        private const string PackagedFontResource = "DigiCreatures/NotoSansSC-Regular";
        private static readonly bool AllowRuntimeTmpFontGeneration = true;

        private static CreatureSubtitleHud instance;
        private static TMP_FontAsset runtimeChineseFont;
        private static bool triedRuntimeChineseFont;
        private static Font runtimeFallbackChineseFont;
        private static bool triedFallbackChineseFont;
        private static readonly string[] PreferredChineseFonts =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Noto Sans CJK SC",
            "Noto Sans SC",
            "Arial Unicode MS"
        };

        [SerializeField] private float defaultVisibleSeconds = 9f;
        [SerializeField] private int maxDialogueCharacters = 180;
        [SerializeField] private int maxIntentCharacters = 240;

        private Canvas canvas;
        private TMP_Text dialogueText;
        private TMP_Text intentText;
        private Text dialogueFallbackText;
        private Text intentFallbackText;
        private float hideAt;

        public static void Show(string speakerName, string dialogue, string intent, float seconds = 0f)
        {
            EnsureInstance().ShowInternal(speakerName, dialogue, intent, seconds);
        }

        public static void SetStatus(string status, string activity)
        {
            CreatureSubtitleHud hud = EnsureInstance();
            if (hud.canvas == null || hud.intentText == null || string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            if (!hud.canvas.enabled)
            {
                hud.SetLine(hud.intentText, hud.intentFallbackText, status);
                hud.canvas.enabled = true;
                hud.hideAt = Time.time + 1.5f;
            }
        }

        private static CreatureSubtitleHud EnsureInstance()
        {
            if (instance != null)
            {
                instance.EnsureUi();
                return instance;
            }

            CreatureSubtitleHud existing = CreatureObjectFinder.FindAnyObjectByType<CreatureSubtitleHud>(true);
            if (existing != null)
            {
                instance = existing;
                instance.EnsureUi();
                return instance;
            }

            GameObject root = new GameObject(HudName);
            instance = root.AddComponent<CreatureSubtitleHud>();
            instance.EnsureUi();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureUi();
        }

        private void LateUpdate()
        {
            EnsureUi();
            if (canvas != null && Time.time > hideAt)
            {
                canvas.enabled = false;
            }
        }

        private void ShowInternal(string speakerName, string dialogue, string intent, float seconds)
        {
            EnsureUi();
            string spoken = TrimForDisplay(dialogue, maxDialogueCharacters);
            string thought = TrimForDisplay(intent, maxIntentCharacters);
            if (string.IsNullOrWhiteSpace(spoken) && string.IsNullOrWhiteSpace(thought))
            {
                return;
            }

            string speaker = string.IsNullOrWhiteSpace(speakerName) ? "数字生物" : speakerName.Trim();
            SetLine(dialogueText, dialogueFallbackText, string.IsNullOrWhiteSpace(spoken) ? $"{speaker}：..." : $"{speaker}：{spoken}");
            SetLine(intentText, intentFallbackText, string.IsNullOrWhiteSpace(thought) ? string.Empty : $"内心：{thought}");
            canvas.enabled = true;
            hideAt = Time.time + Mathf.Max(1f, seconds > 0f ? seconds : defaultVisibleSeconds);
        }

        private void EnsureUi()
        {
            if (canvas != null && dialogueText != null && intentText != null && dialogueFallbackText != null && intentFallbackText != null)
            {
                ApplyRuntimeFont(dialogueText);
                ApplyRuntimeFont(intentText);
                ApplyFallbackFont(dialogueFallbackText);
                ApplyFallbackFont(intentFallbackText);
                return;
            }

            canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform rootRect = GetComponent<RectTransform>();
            if (rootRect == null)
            {
                rootRect = gameObject.AddComponent<RectTransform>();
            }

            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            GameObject panel = EnsureChild(transform, "字幕面板");
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 26f);
            panelRect.sizeDelta = new Vector2(1280f, 132f);

            Image background = panel.GetComponent<Image>();
            if (background == null)
            {
                background = panel.AddComponent<Image>();
            }

            background.color = new Color(0.02f, 0.025f, 0.03f, 0.76f);
            background.raycastTarget = false;

            dialogueText = EnsureText(panel.transform, "说话", new Vector2(26f, 58f), new Vector2(-26f, -14f), 34f, FontStyles.Bold);
            intentText = EnsureText(panel.transform, "内心", new Vector2(26f, 16f), new Vector2(-26f, -74f), 22f, FontStyles.Italic);
            dialogueFallbackText = EnsureFallbackText(panel.transform, "说话Fallback", new Vector2(26f, 58f), new Vector2(-26f, -14f), 34, FontStyle.Bold);
            intentFallbackText = EnsureFallbackText(panel.transform, "内心Fallback", new Vector2(26f, 16f), new Vector2(-26f, -74f), 22, FontStyle.Italic);
            canvas.enabled = false;
        }

        private void SetLine(TMP_Text tmp, Text fallback, string value)
        {
            bool useTmp = ResolveRuntimeChineseFont() != null;
            if (tmp != null)
            {
                tmp.enabled = useTmp;
                tmp.text = useTmp ? value : string.Empty;
            }

            if (fallback != null)
            {
                fallback.enabled = !useTmp;
                fallback.text = useTmp ? string.Empty : value;
            }
        }

        private static TMP_Text EnsureText(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, float fontSize, FontStyles style)
        {
            GameObject child = EnsureChild(parent, name);
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            TMP_Text text = child.GetComponent<TMP_Text>();
            if (text == null)
            {
                text = child.AddComponent<TextMeshProUGUI>();
            }

            text.fontSize = fontSize;
            text.fontStyle = style;
            ApplyRuntimeFont(text);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(14f, fontSize - 10f);
            text.fontSizeMax = fontSize;
            text.raycastTarget = false;
            text.enabled = ResolveRuntimeChineseFont() != null;
            return text;
        }

        private static void ApplyRuntimeFont(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset font = ResolveRuntimeChineseFont();
            if (font != null)
            {
                text.font = font;
            }
        }

        private static TMP_FontAsset ResolveRuntimeChineseFont()
        {
            if (runtimeChineseFont != null)
            {
                return runtimeChineseFont;
            }

            if (!AllowRuntimeTmpFontGeneration)
            {
                return null;
            }

            if (triedRuntimeChineseFont)
            {
                return null;
            }

            triedRuntimeChineseFont = true;
            Font packagedFont = Resources.Load<Font>(PackagedFontResource);
            TMP_FontAsset packagedAsset = CreateTmpFontAsset(packagedFont, "DigiCreatures Packaged Chinese TMP Font");
            if (packagedAsset != null)
            {
                runtimeChineseFont = packagedAsset;
                return runtimeChineseFont;
            }

            foreach (string fontName in PreferredChineseFonts)
            {
                Font osFont = null;
                try
                {
                    osFont = Font.CreateDynamicFontFromOSFont(fontName, 36);
                }
                catch (System.Exception)
                {
                    osFont = null;
                }

                if (osFont == null)
                {
                    continue;
                }

                TMP_FontAsset asset = CreateTmpFontAsset(osFont, "DigiCreatures Runtime Chinese TMP Font");

                if (asset == null)
                {
                    continue;
                }

                runtimeChineseFont = asset;
                return runtimeChineseFont;
            }

            return null;
        }

        private static TMP_FontAsset CreateTmpFontAsset(Font font, string assetName)
        {
            if (font == null)
            {
                return null;
            }

            try
            {
                TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
                if (asset == null)
                {
                    return null;
                }

                asset.name = assetName;
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                return asset;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"创建 TMP 中文字体失败：{font.name}。{ex.Message}");
                return null;
            }
        }

        private static Text EnsureFallbackText(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, int fontSize, FontStyle style)
        {
            GameObject child = EnsureChild(parent, name);
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Text text = child.GetComponent<Text>();
            if (text == null)
            {
                text = child.AddComponent<Text>();
            }

            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(14, fontSize - 10);
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
            ApplyFallbackFont(text);
            text.enabled = ResolveRuntimeChineseFont() == null;
            return text;
        }

        private static void ApplyFallbackFont(Text text)
        {
            if (text == null)
            {
                return;
            }

            Font font = ResolveFallbackChineseFont();
            if (font != null)
            {
                text.font = font;
            }
        }

        private static Font ResolveFallbackChineseFont()
        {
            if (runtimeFallbackChineseFont != null)
            {
                return runtimeFallbackChineseFont;
            }

            if (triedFallbackChineseFont)
            {
                return null;
            }

            triedFallbackChineseFont = true;
            Font packagedFont = Resources.Load<Font>(PackagedFontResource);
            if (packagedFont != null)
            {
                runtimeFallbackChineseFont = packagedFont;
                return runtimeFallbackChineseFont;
            }

            foreach (string fontName in PreferredChineseFonts)
            {
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(fontName, 36);
                    if (font != null)
                    {
                        runtimeFallbackChineseFont = font;
                        return runtimeFallbackChineseFont;
                    }
                }
                catch (System.Exception)
                {
                    // Keep trying other installed font names.
                }
            }

            runtimeFallbackChineseFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return runtimeFallbackChineseFont;
        }

        private static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<RectTransform>();
            return child;
        }

        private static string TrimForDisplay(string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return maxCharacters > 0 && trimmed.Length > maxCharacters
                ? trimmed.Substring(0, maxCharacters - 1) + "..."
                : trimmed;
        }

    }
}
