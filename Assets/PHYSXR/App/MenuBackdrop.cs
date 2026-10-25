using System;
using System.Collections.Generic;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PHYSXR.App
{
    // The welcome screen's moving backdrop: Blackthorn House at night, built
    // from MenuArt layers inside its own nested canvas (so its per-frame
    // motion never rebuilds the menu's text).
    //
    //   sky + moon, slow clouds, the house and trees, lit windows that breathe
    //   and occasionally go dark, two gate lamps, two mist layers and light
    //   rain moving at different speeds, a little drifting dust, restrained
    //   pointer parallax, a rare lightning flash with distant thunder, and now
    //   and then a figure standing in one upper window.
    //
    // Everything is understated: no camera moves, flashes are short and dim.
    // Until the art is generated the backdrop is plain dark (ArtReady false).
    public class MenuBackdrop : MonoBehaviour
    {
        private RectTransform scene;
        private RawImage sky, clouds, house, fogBack, fogFront, rain;
        private Image flash, vignette, leftShade, rightShade, figure;
        private readonly List<Image> windowGlows = new List<Image>();
        private readonly List<Image> windowPanes = new List<Image>();
        private readonly List<float> windowSeed = new List<float>();
        private readonly List<Image> lamps = new List<Image>();
        private readonly List<RectTransform> motes = new List<RectTransform>();
        private readonly List<Image> moteImages = new List<Image>();
        private readonly List<Vector3> moteState = new List<Vector3>();
        private RectTransform houseLayer, fogBackLayer, fogFrontLayer, skyLayer;
        private Action thunder;
        private bool built;
        private float nextFlash, flashAt = -100f, thunderAt = -1f;
        private float nextFigure, figureStart = -100f, figureDuration;
        private int darkWindow = -1;
        private float darkUntil, nextDark;
        private Vector2 parallax;

        public bool ArtReady => built;

        // 0 = black, 1 = fully visible. Driven by MainMenu's reveal and fades.
        public float Visibility { get; set; } = 1f;

        // True once the title has revealed; the figure and lightning wait for it.
        public bool Ambient { get; set; }

        private CanvasGroup group;

        // root: a full-screen rect this component lives on.
        public void Initialize(RectTransform root, Action onThunder)
        {
            thunder = onThunder;
            Canvas nested = root.gameObject.AddComponent<Canvas>();
            nested.overrideSorting = false;
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            UIKit.Image(root, "Base", new Color(0.01f, 0.012f, 0.018f));

            scene = UIKit.Fill(root, "Scene");
            AspectRatioFitter fit = scene.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)MenuArt.SceneW / MenuArt.SceneH;

            leftShade = Overlay(root, "LeftShade");
            rightShade = Overlay(root, "RightShade");
            vignette = Overlay(root, "Vignette");
            flash = UIKit.Image(root, "Flash", new Color(0.75f, 0.8f, 0.95f, 0f));

            MenuArt.Begin();
            nextFlash = Time.unscaledTime + UnityEngine.Random.Range(14f, 22f);
            nextFigure = Time.unscaledTime + UnityEngine.Random.Range(22f, 34f);
            nextDark = Time.unscaledTime + UnityEngine.Random.Range(10f, 18f);
        }

        private static Image Overlay(RectTransform parent, string name)
        {
            Image i = UIKit.Image(parent, name, new Color(0f, 0f, 0f, 0f));
            i.enabled = false;
            return i;
        }

        private void Build(MenuArt.Textures t)
        {
            built = true;
            skyLayer = Layer("SkyLayer", 1.03f);
            sky = Raw(skyLayer, "Sky", t.Sky, Color.white);
            clouds = Raw(skyLayer, "Clouds", t.Clouds, new Color(1f, 1f, 1f, 0.65f));
            clouds.uvRect = new Rect(0f, 0f, 1.6f, 1f);

            fogBackLayer = Layer("FogBack", 1.06f);
            fogBack = Raw(fogBackLayer, "Mist", t.Fog, new Color(0.7f, 0.74f, 0.8f, 0.32f));
            Anchor(fogBack.rectTransform, 0f, 0.18f, 1f, 0.42f);
            fogBack.uvRect = new Rect(0f, 0f, 1.4f, 1f);

            houseLayer = Layer("HouseLayer", 1.04f);
            MenuArt.Generated g = t.Layout;
            for (int i = 0; i < g.Windows.Count; i++)
            {
                Rect r = g.Windows[i];
                Image glow = UIKit.Image(houseLayer, "WindowGlow" + i, new Color(1f, 0.62f, 0.26f, 0f), UIKit.SoftDot);
                Anchor(glow.rectTransform, r.xMin - r.width * 0.8f, r.yMin - r.height * 0.55f, r.xMax + r.width * 0.8f, r.yMax + r.height * 0.55f);
                windowGlows.Add(glow);
                windowSeed.Add(UnityEngine.Random.Range(0f, 100f));
            }
            house = Raw(houseLayer, "House", t.House, Color.white);
            for (int i = 0; i < g.Windows.Count; i++)
            {
                Rect r = g.Windows[i];
                Image pane = UIKit.Image(houseLayer, "WindowLight" + i, new Color(1f, 0.7f, 0.35f, 0f));
                Anchor(pane.rectTransform, r.xMin, r.yMin, r.xMax, r.yMax);
                windowPanes.Add(pane);
            }
            if (g.FigureWindow >= 0)
            {
                Rect r = g.Windows[g.FigureWindow];
                var tex = t.Figure;
                figure = UIKit.Image(houseLayer, "Figure", new Color(1f, 1f, 1f, 0f),
                    Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f)));
                Anchor(figure.rectTransform, r.xMin + r.width * 0.18f, r.yMin, r.xMax - r.width * 0.18f, r.yMax - r.height * 0.06f);
            }
            foreach (Vector2 l in g.Lamps)
            {
                Image lamp = UIKit.Image(houseLayer, "Lamp", new Color(1f, 0.66f, 0.3f, 0.5f), UIKit.SoftDot);
                Anchor(lamp.rectTransform, l.x - 0.007f, l.y - 0.012f, l.x + 0.007f, l.y + 0.012f);
                lamps.Add(lamp);
            }

            fogFrontLayer = Layer("FogFront", 1.08f);
            fogFront = Raw(fogFrontLayer, "Mist", t.Fog, new Color(0.62f, 0.66f, 0.72f, 0.4f));
            Anchor(fogFront.rectTransform, 0f, -0.04f, 1f, 0.3f);
            fogFront.uvRect = new Rect(0.3f, 0f, 1.1f, 1f);
            rain = Raw(fogFrontLayer, "Rain", t.Rain, new Color(1f, 1f, 1f, 0.06f));
            rain.uvRect = new Rect(0f, 0f, 6f, 3.4f);

            for (int i = 0; i < 14; i++)
            {
                Image m = UIKit.Image(fogFrontLayer, "Mote", new Color(1f, 0.9f, 0.75f, 0f), UIKit.SoftDot);
                RectTransform rt = m.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.Range(0.05f, 0.7f));
                float s = UnityEngine.Random.Range(3f, 7f);
                rt.sizeDelta = new Vector2(s, s);
                motes.Add(rt);
                moteImages.Add(m);
                moteState.Add(new Vector3(UnityEngine.Random.Range(-0.004f, 0.004f), UnityEngine.Random.Range(0.001f, 0.005f), UnityEngine.Random.Range(0f, 10f)));
            }

            Sprite shade = Sprite.Create(t.Shade, new Rect(0, 0, t.Shade.width, t.Shade.height), new Vector2(0.5f, 0.5f));
            leftShade.sprite = shade;
            leftShade.color = new Color(0f, 0f, 0f, 0.82f);
            leftShade.enabled = true;
            Anchor(leftShade.rectTransform, 0f, 0f, 0.62f, 1f);
            rightShade.sprite = shade;
            rightShade.color = new Color(0f, 0f, 0f, 0.6f);
            rightShade.enabled = true;
            Anchor(rightShade.rectTransform, 0.5f, 0f, 1f, 1f);
            rightShade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            vignette.sprite = Sprite.Create(t.Vignette, new Rect(0, 0, t.Vignette.width, t.Vignette.height), new Vector2(0.5f, 0.5f));
            vignette.color = Color.white;
            vignette.enabled = true;
        }

        private RectTransform Layer(string name, float scale)
        {
            RectTransform r = UIKit.Fill(scene, name);
            r.localScale = new Vector3(scale, scale, 1f);
            return r;
        }

        private static RawImage Raw(Transform parent, string name, Texture tex, Color color)
        {
            RectTransform rt = UIKit.Fill(parent, name);
            RawImage r = rt.gameObject.AddComponent<RawImage>();
            r.texture = tex;
            r.color = color;
            r.raycastTarget = false;
            return r;
        }

        private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (!built)
            {
                MenuArt.Textures t = MenuArt.TryGet();
                if (t != null)
                    Build(t);
            }
            if (group != null)
                group.alpha = Visibility;
            if (!built)
                return;

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            // Pointer parallax, eased and small.
            Vector2 target = Vector2.zero;
            if (Mouse.current != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 m = Mouse.current.position.ReadValue();
                target = new Vector2(Mathf.Clamp01(m.x / Screen.width) - 0.5f, Mathf.Clamp01(m.y / Screen.height) - 0.5f);
            }
            parallax = Vector2.Lerp(parallax, target, 1f - Mathf.Exp(-dt * 1.5f));
            float breathe = Mathf.Sin(now * 0.07f);
            skyLayer.anchoredPosition = new Vector2(-parallax.x * 6f + breathe * 4f, -parallax.y * 3f);
            houseLayer.anchoredPosition = new Vector2(-parallax.x * 12f + breathe * 6f, -parallax.y * 6f);
            fogBackLayer.anchoredPosition = new Vector2(-parallax.x * 16f, -parallax.y * 6f);
            fogFrontLayer.anchoredPosition = new Vector2(-parallax.x * 26f, -parallax.y * 9f);

            clouds.uvRect = new Rect(now * 0.0035f, 0f, 1.6f, 1f);
            fogBack.uvRect = new Rect(now * 0.006f, 0f, 1.4f, 1f);
            fogFront.uvRect = new Rect(0.3f - now * 0.011f, 0f, 1.1f, 1f);
            rain.uvRect = new Rect(now * 0.12f, now * 0.9f, 6f, 3.4f);

            UpdateWindows(now);
            UpdateMotes(dt);
            UpdateLightning(now);
            UpdateFigure(now);
        }

        private void UpdateWindows(float now)
        {
            if (now >= nextDark && Ambient)
            {
                darkWindow = UnityEngine.Random.Range(0, windowPanes.Count);
                darkUntil = now + UnityEngine.Random.Range(1.5f, 3.5f);
                nextDark = now + UnityEngine.Random.Range(12f, 24f);
            }
            for (int i = 0; i < windowPanes.Count; i++)
            {
                float s = windowSeed[i];
                float breathe = 0.82f + 0.18f * Mathf.PerlinNoise(s, now * (0.6f + (i % 3) * 0.25f));
                if (i == darkWindow && now < darkUntil)
                    breathe *= 0.15f;
                windowPanes[i].color = new Color(1f, 0.72f, 0.38f, 0.1f * breathe);
                windowGlows[i].color = new Color(1f, 0.6f, 0.24f, 0.06f * breathe);
            }
            for (int i = 0; i < lamps.Count; i++)
            {
                float f = 0.7f + 0.3f * Mathf.PerlinNoise(i * 7.1f, now * 2.3f);
                lamps[i].color = new Color(1f, 0.7f, 0.35f, 0.3f * f);
            }
        }

        private void UpdateMotes(float dt)
        {
            for (int i = 0; i < motes.Count; i++)
            {
                RectTransform rt = motes[i];
                Vector3 st = moteState[i];
                Vector2 a = rt.anchorMin + new Vector2(st.x, st.y) * dt;
                if (a.y > 0.75f || a.x < -0.02f || a.x > 1.02f)
                    a = new Vector2(UnityEngine.Random.value, 0.05f);
                rt.anchorMin = rt.anchorMax = a;
                st.z += dt;
                moteState[i] = st;
                float alpha = 0.22f * Mathf.Clamp01(Mathf.Sin(st.z * 0.35f) * 0.5f + 0.5f) * Mathf.Clamp01((0.75f - a.y) * 4f);
                moteImages[i].color = new Color(1f, 0.88f, 0.7f, alpha);
            }
        }

        private void UpdateLightning(float now)
        {
            if (Ambient && now >= nextFlash)
            {
                flashAt = now;
                nextFlash = now + UnityEngine.Random.Range(16f, 34f);
                thunderAt = now + UnityEngine.Random.Range(0.6f, 1.8f);
                if (now - figureStart > figureDuration && UnityEngine.Random.value < 0.35f)
                    StartFigure(now, 1.4f);
            }
            if (thunderAt > 0f && now >= thunderAt)
            {
                thunderAt = -1f;
                thunder?.Invoke();
            }
            float since = now - flashAt;
            float k = since < 0.05f ? 0.22f : since < 0.12f ? 0.02f : since < 0.18f ? 0.16f : since < 0.6f ? Mathf.Lerp(0.12f, 0f, (since - 0.18f) / 0.42f) : 0f;
            flash.color = new Color(0.75f, 0.8f, 0.95f, k);
            float sk = Mathf.Min(1f, 0.86f + k * 0.9f);
            sky.color = new Color(sk, sk, sk, 1f);
        }

        private void StartFigure(float now, float duration)
        {
            figureStart = now;
            figureDuration = duration;
        }

        private void UpdateFigure(float now)
        {
            if (figure == null)
                return;
            if (Ambient && now >= nextFigure)
            {
                StartFigure(now, UnityEngine.Random.Range(7f, 10f));
                nextFigure = now + UnityEngine.Random.Range(35f, 60f);
            }
            float t = now - figureStart;
            float a = 0f;
            if (t >= 0f && t < figureDuration)
            {
                float fadeIn = Mathf.Min(2.5f, figureDuration * 0.3f), fadeOut = Mathf.Min(3f, figureDuration * 0.4f);
                a = Mathf.Clamp01(Mathf.Min(t / fadeIn, (figureDuration - t) / fadeOut)) * 0.9f;
            }
            figure.color = new Color(1f, 1f, 1f, a);
            figure.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(now * 0.4f) * 0.6f, 0f);
        }
    }
}
