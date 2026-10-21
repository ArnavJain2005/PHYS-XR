using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PHYSXR.Game.UI
{
    // Small builder for the game's uGUI screens, so menus and the HUD share
    // one look: a serif title face, a clean body face, dark translucent panels
    // and amber highlights. Everything is built in code (no prefabs or font
    // assets): fonts come from the operating system with Unity's built-in font
    // as the fallback. Generated textures and sprites are cached for the
    // process lifetime.
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.86f, 0.84f, 0.78f);
        public static readonly Color Dim = new Color(0.62f, 0.6f, 0.56f);
        public static readonly Color Accent = new Color(0.95f, 0.66f, 0.32f);
        public static readonly Color PanelColor = new Color(0.02f, 0.02f, 0.025f, 0.86f);
        public static readonly Color Blood = new Color(0.45f, 0.02f, 0.02f);

        private static Font title;
        private static Font body;
        private static Sprite square;
        private static Sprite ring;
        private static Sprite softDot;

        public static Font TitleFont => title != null ? title : title = LoadFont(new[] { "Georgia", "Garamond", "Palatino Linotype", "Times New Roman" }, 64);

        public static Font BodyFont => body != null ? body : body = LoadFont(new[] { "Segoe UI", "Calibri", "Arial" }, 32);

        private static Font LoadFont(string[] names, int size)
        {
            try
            {
                Font os = Font.CreateDynamicFontFromOSFont(names, size);
                if (os != null)
                    return os;
            }
            catch (Exception)
            {
                // Headless or a platform without OS fonts.
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static Sprite Square => square != null ? square : square = MakeSprite(4, (x, y) => 1f);

        // A 128px ring for radial fills (the lantern indicator).
        public static Sprite Ring => ring != null ? ring : ring = MakeSprite(128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((r - 0.72f) / 0.03f) * Mathf.Clamp01((0.98f - r) / 0.03f);
        });

        public static Sprite SoftDot => softDot != null ? softDot : softDot = MakeSprite(64, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - r) / 0.35f);
        });

        private static Sprite MakeSprite(int size, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        // An EventSystem with the Input System UI module, unless one exists.
        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        public static Canvas Canvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Fill(Transform parent, string name)
        {
            RectTransform rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            RectTransform rt = Fill(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : Square;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor anchor, bool titleFace = false)
        {
            RectTransform rt = Fill(parent, name);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = titleFace ? TitleFont : BodyFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            return t;
        }

        public static Shadow Soften(Text t)
        {
            Shadow s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.85f);
            s.effectDistance = new Vector2(1.5f, -1.5f);
            return s;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.UpperCenter)
        {
            RectTransform rt = Fill(parent, name);
            VerticalLayoutGroup v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlHeight = false;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            return v;
        }

        public static Button Button(Transform parent, string label, Action onClick, float width = 380f, float height = 54f)
        {
            RectTransform rt = Rect(parent, "Button_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(width, height));
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Square;
            bg.color = Color.white;
            Button b = rt.gameObject.AddComponent<Button>();
            ColorBlock colors = b.colors;
            colors.normalColor = new Color(0.08f, 0.08f, 0.09f, 0.75f);
            colors.highlightedColor = new Color(0.42f, 0.27f, 0.12f, 0.95f);
            colors.selectedColor = new Color(0.36f, 0.23f, 0.1f, 0.95f);
            colors.pressedColor = new Color(0.7f, 0.45f, 0.2f, 1f);
            colors.disabledColor = new Color(0.08f, 0.08f, 0.08f, 0.35f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = bg;
            Text t = Label(rt, "Label", label, 26, Ink, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            b.onClick.AddListener(() => onClick?.Invoke());
            return b;
        }

        public static Slider Slider(Transform parent, string label, float min, float max, float value, Action<float> onChange, float width = 520f)
        {
            RectTransform row = Rect(parent, "Slider_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(width, 48f));
            Text name = Label(row, "Label", label, 22, Ink, TextAnchor.MiddleLeft);
            ((RectTransform)name.transform).anchorMax = new Vector2(0.42f, 1f);

            RectTransform track = Rect(row, "Track", new Vector2(0.45f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 8f));
            Image trackImg = track.gameObject.AddComponent<Image>();
            trackImg.sprite = Square;
            trackImg.color = new Color(1f, 1f, 1f, 0.15f);

            RectTransform fillArea = Fill(track, "FillArea");
            RectTransform fill = Fill(fillArea, "Fill");
            Image fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = Square;
            fillImg.color = Accent;

            RectTransform handleArea = Fill(track, "HandleArea");
            RectTransform handle = Rect(handleArea, "Handle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
            Image handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = SoftDot;
            handleImg.color = Ink;

            Slider s = track.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            s.onValueChanged.AddListener(v => onChange?.Invoke(v));
            ColorBlock colors = s.colors;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            s.colors = colors;
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange, float width = 520f)
        {
            RectTransform row = Rect(parent, "Toggle_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(width, 44f));
            Text name = Label(row, "Label", label, 22, Ink, TextAnchor.MiddleLeft);
            ((RectTransform)name.transform).anchorMax = new Vector2(0.8f, 1f);

            RectTransform box = Rect(row, "Box", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            Image boxImg = box.gameObject.AddComponent<Image>();
            boxImg.sprite = Square;
            boxImg.color = new Color(1f, 1f, 1f, 0.18f);
            RectTransform check = Rect(box, "Check", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            Image checkImg = check.gameObject.AddComponent<Image>();
            checkImg.sprite = Square;
            checkImg.color = Accent;

            Toggle t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = boxImg;
            t.graphic = checkImg;
            t.isOn = value;
            t.onValueChanged.AddListener(v => onChange?.Invoke(v));
            ColorBlock colors = t.colors;
            colors.highlightedColor = new Color(1f, 0.8f, 0.5f);
            colors.selectedColor = new Color(1f, 0.8f, 0.5f);
            t.colors = colors;
            return t;
        }

        public static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null)
                return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
