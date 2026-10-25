using System;
using System.Collections.Generic;
using PHYSXR.Communication;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Dev;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Settings;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PHYSXR.App
{
    // The start screen (spec section C: "Start screen to pick the condition,
    // enter a trial ID and press Start"). Lives on the persistent [Systems]
    // object and is shown whenever no game scene is loaded.
    //
    //   Begin              starts a trial with the chosen condition and ID
    //   Sensors            Virtual only / Simulated sensors /
    //                      Physical - USB serial / Physical - UDP
    //   Serial port        AUTO or a COM port (USB serial mode only)
    //   Trial ID           free text, pre-filled with a timestamp
    //   Settings, Controls, Quit
    //
    // Layout: two independent, anchored regions that never overlap. The left
    // column holds the identity (brand, title, subtitle) and the controls; the
    // title auto-sizes to the column, so it can never run into the right
    // column, which holds the sensor panel (MenuSensorPanel, fed by
    // SensorPanelModel from the real diagnostics). The split adapts to the
    // window's aspect ratio (see ApplyLayout); at narrow ratios the panel turns
    // compact, and on near-square or portrait windows it moves below the menu.
    //
    // In USB serial mode the link is opened as soon as the mode is selected,
    // so the hardware can be checked before Begin.
    //
    // Presentation: MenuBackdrop (the house at night, in motion),
    // MenuSoundscape (the only menu audio), MenuButtonFx (hover/focus). The
    // first show plays a cinematic reveal (skippable with any key or click);
    // later returns use a short fade. Begin fades to black with a cue that
    // carries into the house, whose own UI fades in from black.
    public class MainMenu : MonoBehaviour
    {
        public const string MenuCaptureArgument = "-physxr-menu-capture";

        private const float RevealLength = 4.8f;
        private const float ReturnLength = 0.9f;
        private const float BeginFade = 1.35f;
        private const float QuitFade = 0.7f;

        private Canvas canvas;
        private RectTransform root, mainPanel, settingsPanel, controlsPanel, column, panelRect, footerRect;
        private CanvasGroup rootGroup, brandGroup, titleGroup, subtitleGroup, footerGroup;
        private RectTransform titleRect, ornament;
        private readonly List<CanvasGroup> itemGroups = new List<CanvasGroup>();
        private readonly List<LayoutElement> itemLayouts = new List<LayoutElement>();
        private Image fader;
        private Text caption, modeLabel, portLabel, footer, titleText;
        private InputField trialField;
        private Button beginButton, modeButton, portButton, settingsBack, controlsBack;
        private GameObject portItem;
        private MenuBackdrop backdrop;
        private MenuSensorPanel sensorPanel;
        private MenuSoundscape sound;
        private GameSettings settings;
        private SensorService sensors;
        private RunCondition condition;
        private PhysicalTransport transport;
        private float nextStatusAt;
        private bool previewDirty;
        private Vector2 laidOutFor;
        private Rect panelRegion;

        private bool shown;
        private bool everShown;
        private bool waitingForArt;
        private float waitStart;
        private float revealStart = -100f;
        private float revealLength = RevealLength;
        private bool revealDone;
        private bool accentQueued;
        private enum Exit { None, Begin, Quit }
        private Exit exit;
        private float exitStart;
        private bool exitFired;

        public event Action<RunCondition, string> BeginRequested;
        public event Action QuitRequested;

        public bool Visible => root != null && root.gameObject.activeSelf;

        // True until the reveal has finished (callers can defer heavy work).
        public bool Revealing => shown && !revealDone;

        public void Initialize(GameSettings settings, SensorService sensors)
        {
            this.settings = settings;
            this.sensors = sensors;
            condition = (RunCondition)Mathf.Clamp(settings.LastCondition, 0, 2);
            transport = (PhysicalTransport)Mathf.Clamp(settings.Transport, 0, 1);

            sound = gameObject.AddComponent<MenuSoundscape>();
            sound.Initialize();

            canvas = UIKit.Canvas(transform, "MenuCanvas", 20);
            root = UIKit.Fill(canvas.transform, "Menu");
            rootGroup = root.gameObject.AddComponent<CanvasGroup>();

            RectTransform backdropRoot = UIKit.Fill(root, "Backdrop");
            backdrop = backdropRoot.gameObject.AddComponent<MenuBackdrop>();
            backdrop.Initialize(backdropRoot, () => sound.Thunder());

            BuildMain();
            BuildSettings();
            BuildControls();

            fader = UIKit.Image(root, "Fader", Color.black);
            RectTransform cap = UIKit.Rect(root, "Caption", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 60f));
            caption = UIKit.Label(cap, "Text", "Entering Blackthorn House", 28, UIKit.Dim, TextAnchor.MiddleCenter, true);
            caption.fontStyle = FontStyle.Italic;
            caption.color = new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0f);

            if (HasArgument(MenuCaptureArgument))
                gameObject.AddComponent<MenuCapture>();

            Show(true);
        }

        // ------------------------------------------------------------ building

        private void BuildMain()
        {
            mainPanel = UIKit.Fill(root, "Main");

            column = UIKit.Rect(mainPanel, "Identity", Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            VerticalLayoutGroup cv = column.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.childAlignment = TextAnchor.UpperLeft;
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandWidth = false;
            cv.childForceExpandHeight = false;
            cv.spacing = 0f;

            RectTransform brand = Item(column, "Brand", 36f, out brandGroup, true);
            Text brandText = UIKit.Label(brand, "Text", "P  H  Y  S  -  X  R", 22, UIKit.Accent, TextAnchor.MiddleLeft);
            brandText.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform title = Item(column, "Title", 132f, out titleGroup, true);
            titleRect = UIKit.Fill(title, "Content");
            titleRect.pivot = new Vector2(0f, 0.5f);
            Text glow = TitleText(titleRect, "Glow", new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.16f));
            Outline halo = glow.gameObject.AddComponent<Outline>();
            halo.effectColor = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.1f);
            halo.effectDistance = new Vector2(3f, -3f);
            titleText = TitleText(titleRect, "Text", UIKit.Ink);
            UIKit.Soften(titleText);

            RectTransform sub = Item(column, "Subtitle", 60f, out subtitleGroup, true);
            Text subText = UIKit.Label(sub, "Text", "Blackthorn House. Three weeks after the caretaker disappeared.", 26, UIKit.Dim, TextAnchor.UpperLeft, true);
            subText.fontStyle = FontStyle.Italic;
            subText.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform orn = Item(column, "Ornament", 26f, out _, true);
            ornament = UIKit.Rect(orn, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 1.5f));
            Image line = ornament.gameObject.AddComponent<Image>();
            line.sprite = UIKit.Square;
            line.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.55f);
            line.raycastTarget = false;
            Item(column, "Gap", 22f, out _, true);

            beginButton = MenuButton("Begin", Begin, true);
            modeButton = MenuButton("Sensors", CycleMode, false);
            modeLabel = modeButton.GetComponentInChildren<Text>();
            portButton = MenuButton("Port", CyclePort, false);
            portLabel = portButton.GetComponentInChildren<Text>();
            portItem = portButton.transform.parent.gameObject;
            trialField = TrialField();
            MenuButton("Settings", () => Open(settingsPanel, settingsBack), false);
            MenuButton("Controls", () => Open(controlsPanel, controlsBack), false);
            MenuButton("Quit", QuitClicked, false);

            footerRect = UIKit.Rect(mainPanel, "Footer", Vector2.zero, Vector2.zero, new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            footerGroup = footerRect.gameObject.AddComponent<CanvasGroup>();
            footer = UIKit.Label(footerRect, "Text", string.Empty, 16, new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.8f), TextAnchor.LowerLeft);

            panelRect = UIKit.Rect(mainPanel, "SensorPanel", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            sensorPanel = panelRect.gameObject.AddComponent<MenuSensorPanel>();
            sensorPanel.Build(panelRect);
            UpdateModeLabel();
        }

        private static Text TitleText(RectTransform parent, string name, Color color)
        {
            Text t = UIKit.Label(parent, name, "THE PRESENCE", 112, color, TextAnchor.MiddleLeft, true);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 44;
            t.resizeTextMaxSize = 112;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1f;
            return t;
        }

        // A column item with a fixed preferred height and its own fade group.
        private RectTransform Item(RectTransform parent, string name, float height, out CanvasGroup group, bool fullWidth)
        {
            RectTransform r = UIKit.Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height * 0.7f;
            if (fullWidth)
            {
                le.preferredWidth = 4000f;
                le.flexibleWidth = 1f;
            }
            group = r.gameObject.AddComponent<CanvasGroup>();
            return r;
        }

        // A menu row: background, thin amber frame, centred label, an underline
        // that reveals on hover/focus and (for Begin) a chevron.
        private Button MenuButton(string label, Action onClick, bool primary)
        {
            RectTransform item = MenuItem("Item_" + label, primary ? 62f : 54f);
            RectTransform rt = UIKit.Fill(item, "Button_" + label);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            Image frame = MenuArt.AddFrame(rt, UIKit.Accent);
            Button b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = bg;
            Text t = UIKit.Label(rt, "Label", label, primary ? 28 : 24, UIKit.Ink, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform under = UIKit.Rect(rt, "Underline", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(160f, 1.5f));
            Image u = under.gameObject.AddComponent<Image>();
            u.sprite = UIKit.Square;
            u.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.85f);
            u.raycastTarget = false;

            Text chevron = null;
            if (primary)
            {
                RectTransform cr = UIKit.Rect(rt, "Chevron", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 1f), new Vector2(30f, 40f));
                chevron = UIKit.Label(cr, "Text", ">", 28, UIKit.Accent, TextAnchor.MiddleCenter);
            }

            MenuButtonFx fx = rt.gameObject.AddComponent<MenuButtonFx>();
            fx.Background = bg;
            fx.Frame = frame;
            fx.Label = t;
            fx.Underline = under;
            fx.Chevron = chevron;
            fx.Primary = primary;
            fx.Hovered = () => sound.Hover();
            b.onClick.AddListener(() => onClick?.Invoke());
            return b;
        }

        private RectTransform MenuItem(string name, float height)
        {
            RectTransform r = UIKit.Rect(column, name, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height * 0.75f;
            le.preferredWidth = 470f;
            le.minWidth = 260f;
            itemLayouts.Add(le);
            itemGroups.Add(r.gameObject.AddComponent<CanvasGroup>());
            Item(column, name + "_Gap", 10f, out _, false);
            return r;
        }

        private InputField TrialField()
        {
            RectTransform item = MenuItem("Item_Trial", 54f);
            RectTransform field = UIKit.Fill(item, "TrialField");
            Image bg = field.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            Image frame = MenuArt.AddFrame(field, UIKit.Accent);
            Text fieldText = UIKit.Label(field, "Text", string.Empty, 24, UIKit.Ink, TextAnchor.MiddleLeft);
            ((RectTransform)fieldText.transform).offsetMin = new Vector2(150f, 0f);
            Text fieldName = UIKit.Label(field, "Name", "Trial ID", 21, UIKit.Dim, TextAnchor.MiddleLeft);
            ((RectTransform)fieldName.transform).offsetMin = new Vector2(24f, 0f);
            InputField input = field.gameObject.AddComponent<InputField>();
            input.textComponent = fieldText;
            input.targetGraphic = bg;
            input.transition = Selectable.Transition.None;
            input.characterLimit = 32;
            input.text = "T" + DateTime.Now.ToString("yyyyMMdd-HHmm");
            MenuButtonFx fx = field.gameObject.AddComponent<MenuButtonFx>();
            fx.Background = bg;
            fx.Frame = frame;
            fx.Hovered = () => sound.Hover();
            return input;
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
                "W A S D        move\nMouse          look\nShift / Space   sprint / jump\nE              interact (fuse, fuse box, doors, notes)\n"
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
            Image dim = panel.gameObject.AddComponent<Image>();
            dim.sprite = UIKit.Square;
            dim.color = new Color(0f, 0f, 0f, 0.45f);
            RectTransform box = UIKit.Rect(panel, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 640f));
            Image bg = box.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            bg.color = new Color(0.022f, 0.022f, 0.03f, 0.97f);
            MenuArt.AddFrame(box, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.3f));
            RectTransform t = UIKit.Rect(box, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(0f, 80f));
            UIKit.Label(t, "Text", title, 48, UIKit.Ink, TextAnchor.MiddleCenter, true);
            panel.gameObject.SetActive(false);
            return panel;
        }

        // ------------------------------------------------------------ layout

        // Two anchored regions. The title auto-sizes inside the left one, so
        // the regions themselves are the overlap guarantee.
        private void ApplyLayout(Vector2 size)
        {
            laidOutFor = size;
            float aspect = size.x / Mathf.Max(1f, size.y);
            bool stacked = aspect < 1.1f;
            bool compact = aspect < 1.45f;

            if (stacked)
            {
                SetAnchors(column, 0.07f, 0.4f, 0.93f, 0.96f);
                panelRegion = Rect.MinMaxRect(0.07f, 0.04f, 0.93f, 0.37f);
                SetAnchors(footerRect, 0.07f, 0.005f, 0.93f, 0.035f);
            }
            else if (compact)
            {
                SetAnchors(column, 0.05f, 0.08f, 0.5f, 0.95f);
                panelRegion = Rect.MinMaxRect(0.54f, 0.07f, 0.965f, 0.93f);
                SetAnchors(footerRect, 0.05f, 0.015f, 0.5f, 0.06f);
            }
            else
            {
                SetAnchors(column, 0.06f, 0.08f, 0.5f, 0.93f);
                panelRegion = Rect.MinMaxRect(0.585f, 0.09f, 0.955f, 0.91f);
                SetAnchors(footerRect, 0.06f, 0.02f, 0.5f, 0.065f);
            }
            sensorPanel.SetCompact(stacked ? 2 : compact ? 1 : 0);
            nextStatusAt = 0f;

            // Short windows: shrink the stack rather than let it overflow.
            float available = size.y * (column.anchorMax.y - column.anchorMin.y);
            float needed = 36f + 132f + 60f + 26f + 22f + 62f + 6f * 54f + 7f * 10f;
            float k = Mathf.Clamp(available / needed, 0.72f, 1f);
            foreach (LayoutElement le in itemLayouts)
                le.preferredHeight = (le.transform == beginButton.transform.parent ? 62f : 54f) * k;
            LayoutRebuilder.MarkLayoutForRebuild(column);
        }

        private static void SetAnchors(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------ show / panels

        private void Open(RectTransform panel, Selectable focus)
        {
            bool changing = !panel.gameObject.activeSelf;
            mainPanel.gameObject.SetActive(panel == mainPanel);
            settingsPanel.gameObject.SetActive(panel == settingsPanel);
            controlsPanel.gameObject.SetActive(panel == controlsPanel);
            if (changing && everShown)
                sound.Select();
            MenuButtonFx.SilentUntil = Time.unscaledTime + 0.1f;
            UIKit.Select(focus);
        }

        public void Show(bool visible)
        {
            if (visible && shown)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            shown = visible;
            root.gameObject.SetActive(visible);
            if (!visible)
            {
                sound.Leave();
                return;
            }

            exit = Exit.None;
            exitFired = false;
            rootGroup.interactable = false;
            mainPanel.gameObject.SetActive(true);
            settingsPanel.gameObject.SetActive(false);
            controlsPanel.gameObject.SetActive(false);
            MenuButtonFx.SilentUntil = Time.unscaledTime + 0.2f;
            UIKit.Select(beginButton);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            previewDirty = true;
            laidOutFor = Vector2.zero;

            bool first = !everShown;
            everShown = true;
            revealLength = first ? RevealLength : ReturnLength;
            revealDone = false;
            accentQueued = first;
            caption.color = new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0f);
            waitingForArt = first;
            waitStart = Time.unscaledTime;
            revealStart = Time.unscaledTime;
            if (first && (Application.isBatchMode || CaptureTour.RequestedFolder() != null))
            {
                // Self-tests and the capture tour see the finished menu at once.
                waitingForArt = false;
                revealStart = Time.unscaledTime - revealLength;
            }
            ApplyReveal(0f);
            sound.Enter(first);
        }

        public void SetFooter(string text)
        {
            if (footer != null)
                footer.text = text;
        }

        // ------------------------------------------------------------ modes

        // Mode order: Virtual only -> Simulated -> Physical USB serial -> Physical UDP.
        private int ModeIndex
        {
            get
            {
                if (condition == RunCondition.VIRTUAL_ONLY) return 0;
                if (condition == RunCondition.SIMULATED) return 1;
                return transport == PhysicalTransport.USB_SERIAL ? 2 : 3;
            }
        }

        private bool SerialSelected => ModeIndex == 2;

        private void CycleMode()
        {
            SetModeIndex((ModeIndex + 1) % 4);
            sound.Select();
        }

        // Also used by MenuCapture to photograph each mode.
        public void SetModeIndex(int index)
        {
            switch (index)
            {
                case 0: condition = RunCondition.VIRTUAL_ONLY; break;
                case 1: condition = RunCondition.SIMULATED; break;
                case 2: condition = RunCondition.PHYSICAL_PLUS_VIRTUAL; transport = PhysicalTransport.USB_SERIAL; break;
                default: condition = RunCondition.PHYSICAL_PLUS_VIRTUAL; transport = PhysicalTransport.UDP; break;
            }
            settings.LastCondition = (int)condition;
            settings.Transport = (int)transport;
            UpdateModeLabel();
            previewDirty = true;
        }

        public int CurrentModeIndex => ModeIndex;

        // Seconds into the current reveal; negative while the art is still loading.
        public float RevealTime => waitingForArt ? -1f : Time.unscaledTime - revealStart;

        // MenuCapture: put keyboard focus on a control ("Begin", "Sensors", "Settings", ...).
        public void FocusForCapture(string label)
        {
            foreach (MenuButtonFx fx in column.GetComponentsInChildren<MenuButtonFx>())
                if (fx.name == "Button_" + label)
                    UIKit.Select(fx.GetComponent<Selectable>());
        }

        // MenuCapture: press Begin as a player would.
        public void BeginForCapture()
        {
            Begin();
        }

        // MenuCapture: 0 main, 1 settings, 2 controls.
        public void OpenForCapture(int which)
        {
            if (which == 1) Open(settingsPanel, settingsBack);
            else if (which == 2) Open(controlsPanel, controlsBack);
            else Open(mainPanel, beginButton);
        }

        // AUTO, then every COM port present right now.
        private void CyclePort()
        {
            var options = new List<string> { GameSettings.AutoSerialPort };
            if (sensors != null)
                foreach (SerialPortInfo p in sensors.ListSerialPorts())
                    options.Add(p.Name);

            int current = options.FindIndex(o => string.Equals(o, settings.SerialPort, StringComparison.OrdinalIgnoreCase));
            settings.SerialPort = options[(current + 1) % options.Count];
            UpdateModeLabel();
            previewDirty = true;
            sound.Select();
        }

        private void UpdateModeLabel()
        {
            modeLabel.text = "Sensors:  " + SensorPanelModel.ModeName((SensorPanelModel.Mode)ModeIndex)
                             + (ModeIndex == 3 ? " " + SensorService.DefaultPort : string.Empty);
            portLabel.text = "Serial port:  " + settings.SerialPort + "  (" + settings.SerialBaud + ")";
            portItem.SetActive(SerialSelected);
            nextStatusAt = 0f;
            if (column != null)
                LayoutRebuilder.MarkLayoutForRebuild(column);
        }

        public SerialLinkSettings SerialSettings()
        {
            return new SerialLinkSettings { PortName = settings.SerialPort, BaudRate = settings.SerialBaud };
        }

        // Opens the link while USB serial is selected so the readings can be
        // checked before Begin; releases it otherwise (unless a trial uses it).
        // Applied from Update, so a self-test that hides the menu before the
        // first frame never opens a COM port.
        private void ApplyLinkPreview()
        {
            if (sensors == null || !Visible)
                return;
            if (SerialSelected)
                sensors.UseSerialLink(SerialSettings());
            else if (!sensors.Configured || sensors.Condition == RunCondition.SIMULATED || sensors.Transport != PhysicalTransport.USB_SERIAL)
                sensors.ReleaseSerialLink();
        }

        // The panel shows the real diagnostics only (SensorPanelModel).
        private void UpdateStatus()
        {
            SensorPanelModel.View view;
            var mode = (SensorPanelModel.Mode)ModeIndex;
            if (mode == SensorPanelModel.Mode.UsbSerial && sensors != null)
            {
                SerialLineReceiver link = sensors.Serial;
                view = SensorPanelModel.ForSerial(link != null, link != null ? link.Status : default, settings.SerialPort, settings.SerialBaud,
                    sensors.Station, sensors.NowMs, sensors.LastRejectReason);
            }
            else
            {
                view = SensorPanelModel.ForMode(mode == SensorPanelModel.Mode.UsbSerial ? SensorPanelModel.Mode.VirtualOnly : mode, SensorService.DefaultPort);
            }
            sensorPanel.Render(view);
            FitPanel();
        }

        // The panel hangs from the top of its region and is as tall as its
        // content, never taller than the region (so it can never reach the
        // left column, whatever it shows).
        private void FitPanel()
        {
            if (panelRegion.width <= 0f)
                return;
            float regionHeight = laidOutFor.y * panelRegion.height;
            panelRect.anchorMin = new Vector2(panelRegion.xMin, panelRegion.yMax);
            panelRect.anchorMax = new Vector2(panelRegion.xMax, panelRegion.yMax);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(0f, Mathf.Clamp(sensorPanel.ContentHeight, 200f, Mathf.Max(200f, regionHeight)));
        }

        // ------------------------------------------------------------ actions

        private void Begin()
        {
            if (exit != Exit.None)
                return;
            sound.Select();
            settings.Save();
            exit = Exit.Begin;
            exitStart = Time.unscaledTime;
            rootGroup.interactable = false;
            sound.BeginCue();
        }

        private void QuitClicked()
        {
            if (exit != Exit.None)
                return;
            sound.Select();
            exit = Exit.Quit;
            exitStart = Time.unscaledTime;
            rootGroup.interactable = false;
            sound.Leave();
        }

        private void FireExit()
        {
            exitFired = true;
            if (exit == Exit.Begin)
            {
                string trial = string.IsNullOrWhiteSpace(trialField.text) ? "T" + DateTime.Now.ToString("yyyyMMdd-HHmm") : trialField.text.Trim();
                BeginRequested?.Invoke(condition, trial);
            }
            else
            {
                QuitRequested?.Invoke();
            }
        }

        // ------------------------------------------------------------ frame

        private void Update()
        {
            if (!Visible)
                return;

            Vector2 size = ((RectTransform)canvas.transform).rect.size;
            if ((size - laidOutFor).sqrMagnitude > 1f)
                ApplyLayout(size);

            if (previewDirty)
            {
                previewDirty = false;
                ApplyLinkPreview();
            }

            if (Time.unscaledTime >= nextStatusAt)
            {
                nextStatusAt = Time.unscaledTime + 0.25f;
                UpdateStatus();
            }

            float now = Time.unscaledTime;
            if (waitingForArt)
            {
                bool ready = backdrop.ArtReady && sound.MusicReady;
                if (ready || now - waitStart > 3f)
                {
                    waitingForArt = false;
                    revealStart = now;
                }
                else
                {
                    revealStart = now;
                }
            }
            else if (!revealDone && AnySkipInput())
            {
                revealStart = now - revealLength;
            }

            float t = now - revealStart;
            if (accentQueued && !waitingForArt && t >= 1.2f)
            {
                accentQueued = false;
                sound.TitleAccent();
            }
            ApplyReveal(t);

            if (!mainPanel.gameObject.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                settings.Save();
                Open(mainPanel, beginButton);
            }

            if (exit != Exit.None)
                UpdateExit(now - exitStart);
        }

        private static bool AnySkipInput()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                   || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        }

        // The cinematic reveal (first show) or a short fade (later shows).
        private void ApplyReveal(float t)
        {
            float k = revealLength / RevealLength;
            float scene = Ease(t, 0f, 2.2f * k);
            backdrop.Visibility = waitingForArt ? 0f : scene;
            brandGroup.alpha = Ease(t, 0.9f * k, 0.9f * k);
            float title = Ease(t, 1.3f * k, 1.7f * k);
            titleGroup.alpha = title;
            float s = Mathf.Lerp(1.035f, 1f, title);
            titleRect.localScale = new Vector3(s, s, 1f);
            subtitleGroup.alpha = Ease(t, 2.6f * k, 0.9f * k);
            ornament.localScale = new Vector3(Mathf.Max(0.001f, Ease(t, 3f * k, 1.1f * k)), 1f, 1f);
            for (int i = 0; i < itemGroups.Count; i++)
                itemGroups[i].alpha = Ease(t, (3.1f + i * 0.08f) * k, 0.5f * k);
            sensorPanel.Group.alpha = Ease(t, 3.5f * k, 0.9f * k);
            footerGroup.alpha = Ease(t, 4.1f * k, 0.7f * k) * 0.9f;

            if (exit == Exit.None)
                fader.color = new Color(0f, 0f, 0f, waitingForArt ? 1f : 1f - scene);
            fader.raycastTarget = false;

            bool finished = !waitingForArt && t >= revealLength;
            if (finished && !revealDone)
            {
                revealDone = true;
                backdrop.Ambient = true;
            }
            if (exit == Exit.None)
                rootGroup.interactable = !waitingForArt && t >= 3.1f * k;
        }

        private void UpdateExit(float t)
        {
            float length = exit == Exit.Begin ? BeginFade : QuitFade;
            float f = Ease(t, 0f, length);
            fader.color = new Color(0f, 0f, 0f, f);
            if (exit == Exit.Begin)
                caption.color = new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, Ease(t, 0.5f, 0.6f) * 0.9f);
            if (!exitFired && t >= length + 0.05f)
                FireExit();
        }

        private static float Ease(float t, float start, float duration)
        {
            float x = duration <= 0f ? (t >= start ? 1f : 0f) : Mathf.Clamp01((t - start) / duration);
            return x * x * (3f - 2f * x);
        }

        private static bool HasArgument(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        }
    }
}
