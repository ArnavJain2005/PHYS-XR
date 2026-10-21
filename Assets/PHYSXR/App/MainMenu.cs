using System;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Settings;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PHYSXR.App
{
    // The start screen (spec section C: "Start screen to pick the condition,
    // enter a trial ID and press Start"). Lives on the persistent [Systems]
    // object and is shown whenever no game scene is loaded.
    //
    //   Begin              starts a trial with the chosen condition and ID
    //   Sensor mode        Virtual only / Simulated sensors / Physical sensors (UDP)
    //   Trial ID           free text, pre-filled with a timestamp
    //   Settings, Controls, Quit
    //
    // The backdrop is the generated coast-and-house image with a slow drift,
    // grain from the vignette and the odd lightning flash.
    public class MainMenu : MonoBehaviour
    {
        private Canvas canvas;
        private RectTransform root, mainPanel, settingsPanel, controlsPanel;
        private Image backdrop, flash;
        private Text modeLabel, footer;
        private InputField trialField;
        private Button beginButton, modeButton, settingsBack, controlsBack;
        private GameSettings settings;
        private RunCondition condition;
        private float nextFlash;
        private float flashAt = -10f;
        private AudioSource ui;

        public event Action<RunCondition, string> BeginRequested;
        public event Action QuitRequested;

        public bool Visible => root != null && root.gameObject.activeSelf;

        public void Initialize(GameSettings settings)
        {
            this.settings = settings;
            condition = (RunCondition)Mathf.Clamp(settings.LastCondition, 0, 2);

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.ignoreListenerPause = true;

            canvas = UIKit.Canvas(transform, "MenuCanvas", 20);
            root = UIKit.Fill(canvas.transform, "Menu");

            backdrop = UIKit.Image(root, "Backdrop", new Color(0.55f, 0.55f, 0.6f));
            backdrop.sprite = Sprite.Create(ProceduralTextures.Get(ProceduralTextures.Id.MenuBackdrop), new Rect(0, 0, 512, 512), new Vector2(0.5f, 0.5f));
            backdrop.preserveAspect = false;
            UIKit.Image(root, "Shade", new Color(0f, 0f, 0f, 0.55f));
            flash = UIKit.Image(root, "Flash", new Color(0.8f, 0.85f, 1f, 0f));

            BuildMain();
            BuildSettings();
            BuildControls();
            Show(true);
            nextFlash = Time.unscaledTime + 4f;
        }

        private void BuildMain()
        {
            mainPanel = UIKit.Fill(root, "Main");

            RectTransform brand = UIKit.Rect(mainPanel, "Brand", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140f, -150f), new Vector2(900f, 40f));
            UIKit.Label(brand, "Text", "P H Y S - X R", 24, UIKit.Accent, TextAnchor.MiddleLeft);
            RectTransform title = UIKit.Rect(mainPanel, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -190f), new Vector2(1100f, 140f));
            Text t = UIKit.Label(title, "Text", "THE PRESENCE", 118, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Soften(t);
            RectTransform tag = UIKit.Rect(mainPanel, "Tagline", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140f, -330f), new Vector2(1100f, 40f));
            Text tl = UIKit.Label(tag, "Text", "Blackthorn House. Three weeks after the caretaker disappeared.", 26, UIKit.Dim, TextAnchor.MiddleLeft, true);
            tl.fontStyle = FontStyle.Italic;

            RectTransform column = UIKit.Rect(mainPanel, "Column", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(140f, 40f), new Vector2(560f, 520f));
            VerticalLayoutGroup v = UIKit.Column(column, "Buttons", 12f, TextAnchor.UpperLeft);
            beginButton = UIKit.Button(v.transform, "Begin", Begin, 460f, 60f);
            modeButton = UIKit.Button(v.transform, "Sensors", CycleMode, 460f, 54f);
            modeLabel = modeButton.GetComponentInChildren<Text>();

            RectTransform field = UIKit.Rect(v.transform, "TrialField", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 54f));
            Image fieldBg = field.gameObject.AddComponent<Image>();
            fieldBg.sprite = UIKit.Square;
            fieldBg.color = new Color(0.08f, 0.08f, 0.09f, 0.75f);
            Text fieldText = UIKit.Label(field, "Text", string.Empty, 24, UIKit.Ink, TextAnchor.MiddleLeft);
            ((RectTransform)fieldText.transform).offsetMin = new Vector2(150f, 0f);
            Text fieldName = UIKit.Label(field, "Name", "Trial ID", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            ((RectTransform)fieldName.transform).offsetMin = new Vector2(18f, 0f);
            trialField = field.gameObject.AddComponent<InputField>();
            trialField.textComponent = fieldText;
            trialField.targetGraphic = fieldBg;
            trialField.characterLimit = 32;
            trialField.text = "T" + DateTime.Now.ToString("yyyyMMdd-HHmm");

            UIKit.Button(v.transform, "Settings", () => Open(settingsPanel, settingsBack), 460f, 54f);
            UIKit.Button(v.transform, "Controls", () => Open(controlsPanel, controlsBack), 460f, 54f);
            UIKit.Button(v.transform, "Quit", () => QuitRequested?.Invoke(), 460f, 54f);

            RectTransform foot = UIKit.Rect(mainPanel, "Footer", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(-280f, 60f));
            footer = UIKit.Label(foot, "Text", string.Empty, 18, UIKit.Dim, TextAnchor.LowerLeft);
            UpdateModeLabel();
        }

        private void BuildSettings()
        {
            settingsPanel = Panel("Settings", "SETTINGS");
            VerticalLayoutGroup v = UIKit.Column(settingsPanel.Find("Box"), "Rows", 14f);
            ((RectTransform)v.transform).offsetMax = new Vector2(0f, -130f);
            UIKit.Slider(v.transform, "Mouse sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, settings.MouseSensitivity, x => settings.MouseSensitivity = x);
            UIKit.Toggle(v.transform, "Invert mouse Y", settings.InvertY, x => settings.InvertY = x);
            UIKit.Slider(v.transform, "Volume", 0f, 1f, settings.MasterVolume, x => { settings.MasterVolume = x; settings.ApplyAudio(); });
            UIKit.Slider(v.transform, "Brightness", -1f, 1f, settings.Brightness, x => settings.Brightness = x);
            UIKit.Toggle(v.transform, "Head bob", settings.HeadBob, x => settings.HeadBob = x);
            UIKit.Toggle(v.transform, "Developer overlay (F1)", settings.DevOverlay, x => settings.DevOverlay = x);
            settingsBack = UIKit.Button(v.transform, "Back", () => { settings.Save(); Open(mainPanel, beginButton); }, 300f);
        }

        private void BuildControls()
        {
            controlsPanel = Panel("Controls", "CONTROLS");
            RectTransform text = UIKit.Rect(controlsPanel.Find("Box"), "Text", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(-120f, -260f));
            UIKit.Label(text, "Label",
                "W A S D        move\nMouse          look\nE              interact (fuse, fuse box, doors, notes)\n"
                + "F / right click   raise the lantern - briefly drives The Hollow back\nEsc            pause\n\n"
                + "Simulated sensors (developer):  1 2 3 presence,  4 confidence,  5 disturbance,\n"
                + "6 resend event,  7 stale packet,  8 reboot,  9 stop/resume.   F1 developer overlay.",
                24, UIKit.Ink, TextAnchor.UpperLeft);
            VerticalLayoutGroup v = UIKit.Column(controlsPanel.Find("Box"), "Rows", 10f, TextAnchor.LowerCenter);
            ((RectTransform)v.transform).offsetMin = new Vector2(0f, 30f);
            controlsBack = UIKit.Button(v.transform, "Back", () => Open(mainPanel, beginButton), 300f);
        }

        private RectTransform Panel(string name, string title)
        {
            RectTransform panel = UIKit.Fill(root, name);
            RectTransform box = UIKit.Rect(panel, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 640f));
            Image bg = box.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            bg.color = new Color(0.02f, 0.02f, 0.025f, 0.92f);
            RectTransform t = UIKit.Rect(box, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(0f, 80f));
            UIKit.Label(t, "Text", title, 48, UIKit.Ink, TextAnchor.MiddleCenter, true);
            panel.gameObject.SetActive(false);
            return panel;
        }

        private void Open(RectTransform panel, Selectable focus)
        {
            mainPanel.gameObject.SetActive(panel == mainPanel);
            settingsPanel.gameObject.SetActive(panel == settingsPanel);
            controlsPanel.gameObject.SetActive(panel == controlsPanel);
            Click();
            UIKit.Select(focus);
        }

        public void Show(bool visible)
        {
            root.gameObject.SetActive(visible);
            if (!visible)
                return;
            Open(mainPanel, beginButton);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void SetFooter(string text)
        {
            if (footer != null)
                footer.text = text;
        }

        private void CycleMode()
        {
            condition = (RunCondition)(((int)condition + 1) % 3);
            settings.LastCondition = (int)condition;
            UpdateModeLabel();
            Click();
        }

        private void UpdateModeLabel()
        {
            string mode;
            switch (condition)
            {
                case RunCondition.SIMULATED: mode = "Simulated sensors"; break;
                case RunCondition.PHYSICAL_PLUS_VIRTUAL: mode = "Physical sensors (UDP " + SensorService.DefaultPort + ")"; break;
                default: mode = "Virtual only"; break;
            }
            modeLabel.text = "Sensors:  " + mode;
        }

        private void Begin()
        {
            Click();
            settings.Save();
            string trial = string.IsNullOrWhiteSpace(trialField.text) ? "T" + DateTime.Now.ToString("yyyyMMdd-HHmm") : trialField.text.Trim();
            BeginRequested?.Invoke(condition, trial);
        }

        private void Click()
        {
            if (ui != null)
                ui.PlayOneShot(ProceduralAudio.Get(AudioCue.UiSelect), GameAudio.Level(AudioCue.UiSelect));
        }

        private void Update()
        {
            if (!Visible)
                return;

            float t = Time.unscaledTime;
            float zoom = 1.08f + Mathf.Sin(t * 0.05f) * 0.04f;
            backdrop.rectTransform.localScale = new Vector3(zoom, zoom, 1f);
            backdrop.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(t * 0.03f) * 30f, Mathf.Cos(t * 0.04f) * 12f);

            if (t >= nextFlash)
            {
                flashAt = t;
                nextFlash = t + UnityEngine.Random.Range(9f, 20f);
                if (ui != null)
                    ui.PlayOneShot(ProceduralAudio.Get(AudioCue.Thunder, UnityEngine.Random.Range(0, 3)), 0.5f);
            }
            float since = t - flashAt;
            float k = since < 0.06f ? 0.5f : since < 0.14f ? 0f : since < 0.5f ? Mathf.Lerp(0.35f, 0f, (since - 0.14f) / 0.36f) : 0f;
            flash.color = new Color(0.8f, 0.85f, 1f, k);
        }
    }
}
