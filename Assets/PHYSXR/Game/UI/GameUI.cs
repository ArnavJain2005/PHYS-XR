using System;
using System.Globalization;
using System.Text;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Player;
using PHYSXR.Game.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PHYSXR.Game.UI
{
    // The game scene's desktop UI, built with uGUI in code. It only reads
    // state and calls GameFlowController; it owns no game rules.
    //
    //   HUD      objective, crosshair, interaction prompt, messages and hints,
    //            lantern charge ring, sensor-status chip, controls hint.
    //   Pause    Resume / Restart from checkpoint / Restart run / Settings /
    //            Main menu / Quit, and the settings panel.
    //   Note     an optional clue's text (E again, moving away or pausing closes it).
    //   Death    red flash, fade, then Continue from checkpoint / Restart / Menu.
    //   Ending   subtitles over the fade, then the end card with run stats.
    //   Dev      F1: condition, freshness, decision, rule, reason, ghost phase.
    //
    // A world-space version is needed for Quest (spec M8); this is desktop only.
    public class GameUI : MonoBehaviour
    {
        private const float MessageSeconds = 3.5f;
        private const float HintSeconds = 7f;
        private const float ControlsHintSeconds = 14f;

        private GameFlowController flow;
        private PlayerInteractor interactor;
        private DefensiveAbility lantern;
        private EndingSequence ending;
        private GameAudio audioPlayer;

        private Canvas canvas;
        private RectTransform hud;
        private Text objective, objectiveHeader, prompt, message, hint, controls, sensorChip, lanternKey, devText, subtitle;
        private Image crosshair, lanternRing, lanternBack, fader, flash;
        private RectTransform pausePanel, settingsPanel, notePanel, deathPanel, endPanel, devPanel, subtitlePanel;
        private Text noteTitle, noteBody, deathSub, endStats;
        private Button resumeButton, checkpointButton, deathContinue, endAgain, settingsBack;
        private Slider sensitivitySlider, volumeSlider, brightnessSlider;
        private Toggle invertToggle, bobToggle;

        private float objectiveFlash;
        private float hintUntil;
        private float deathStartedAt = -1f;
        private ReadableNote openNote;
        private Vector3 noteOpenedAt;
        private float noteOpenedTime;
        private bool devVisible;
        private bool devInitialized;
        private float devRefreshAt;
        private readonly StringBuilder devBuilder = new StringBuilder(512);

        public event Action SettingsChanged;

        public void Initialize(GameFlowController flow, PlayerInteractor interactor, DefensiveAbility lantern, EndingSequence ending, GameAudio audioPlayer = null)
        {
            this.flow = flow;
            this.interactor = interactor;
            this.lantern = lantern;
            this.ending = ending;
            this.audioPlayer = audioPlayer;

            UIKit.EnsureEventSystem(transform);
            canvas = UIKit.Canvas(transform, "GameCanvas", 10);
            BuildHud();
            BuildPause();
            BuildSettings();
            BuildNote();
            BuildDeath();
            BuildEnding();
            BuildDev();
            flash = UIKit.Image(canvas.transform, "Flash", new Color(UIKit.Blood.r, UIKit.Blood.g, UIKit.Blood.b, 0f));
            fader = UIKit.Image(canvas.transform, "Fader", new Color(0f, 0f, 0f, 1f));
            subtitlePanel.SetAsLastSibling();
            endPanel.SetAsLastSibling();
            deathPanel.SetAsLastSibling();

            flow.MessageRaised += OnMessage;
            flow.HintRaised += OnHint;
            flow.NoteOpened += OnNote;
            flow.DeathStarted += OnDeath;
            flow.ObjectiveChanged += OnObjective;
        }

        private void OnDestroy()
        {
            if (flow == null)
                return;
            flow.MessageRaised -= OnMessage;
            flow.HintRaised -= OnHint;
            flow.NoteOpened -= OnNote;
            flow.DeathStarted -= OnDeath;
            flow.ObjectiveChanged -= OnObjective;
        }

        private GameSettings Settings => flow.Services?.Settings;

        // ------------------------------------------------------------ building

        private void BuildHud()
        {
            hud = UIKit.Fill(canvas.transform, "HUD");

            RectTransform obj = UIKit.Rect(hud, "Objective", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -40f), new Vector2(760f, 110f));
            objectiveHeader = UIKit.Label(obj, "Header", "OBJECTIVE", 18, UIKit.Accent, TextAnchor.UpperLeft);
            UIKit.Soften(objectiveHeader);
            objective = UIKit.Label(obj, "Text", string.Empty, 30, UIKit.Ink, TextAnchor.UpperLeft, true);
            ((RectTransform)objective.transform).offsetMax = new Vector2(0f, -26f);
            UIKit.Soften(objective);

            RectTransform cross = UIKit.Rect(hud, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f));
            crosshair = cross.gameObject.AddComponent<Image>();
            crosshair.sprite = UIKit.SoftDot;
            crosshair.color = new Color(1f, 1f, 1f, 0.6f);
            crosshair.raycastTarget = false;

            prompt = CenteredLabel(hud, "Prompt", new Vector2(0f, -70f), 28, UIKit.Ink);
            message = CenteredLabel(hud, "Message", new Vector2(0f, -250f), 28, UIKit.Ink);
            hint = CenteredLabel(hud, "Hint", new Vector2(0f, 220f), 26, UIKit.Accent);

            RectTransform ctl = UIKit.Rect(hud, "Controls", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 40f), new Vector2(1100f, 40f));
            controls = UIKit.Label(ctl, "Text", "WASD move    Mouse look    E interact    F lantern    Esc pause", 22, UIKit.Dim, TextAnchor.LowerLeft);
            UIKit.Soften(controls);

            RectTransform chip = UIKit.Rect(hud, "Sensors", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -36f), new Vector2(620f, 34f));
            sensorChip = UIKit.Label(chip, "Text", string.Empty, 18, UIKit.Dim, TextAnchor.UpperRight);
            UIKit.Soften(sensorChip);

            RectTransform lamp = UIKit.Rect(hud, "Lantern", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-56f, 48f), new Vector2(86f, 86f));
            lanternBack = UIKit.Image(lamp, "Back", new Color(1f, 1f, 1f, 0.14f), UIKit.Ring);
            lanternRing = UIKit.Image(lamp, "Charge", new Color(1f, 0.75f, 0.42f, 0.9f), UIKit.Ring);
            lanternRing.type = Image.Type.Filled;
            lanternRing.fillMethod = Image.FillMethod.Radial360;
            lanternRing.fillOrigin = (int)Image.Origin360.Top;
            lanternKey = UIKit.Label(lamp, "Key", "F", 30, UIKit.Ink, TextAnchor.MiddleCenter, true);
        }

        private static Text CenteredLabel(Transform parent, string name, Vector2 position, int size, Color color)
        {
            RectTransform rt = UIKit.Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(1400f, 60f));
            Text t = UIKit.Label(rt, "Text", string.Empty, size, color, TextAnchor.MiddleCenter);
            UIKit.Soften(t);
            return t;
        }

        private RectTransform Panel(string name, Vector2 size, float alpha = 0.86f)
        {
            RectTransform dim = UIKit.Fill(canvas.transform, name);
            Image shade = dim.gameObject.AddComponent<Image>();
            shade.sprite = UIKit.Square;
            shade.color = new Color(0f, 0f, 0f, 0.55f);
            RectTransform box = UIKit.Rect(dim, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image bg = box.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            bg.color = new Color(UIKit.PanelColor.r, UIKit.PanelColor.g, UIKit.PanelColor.b, alpha);
            dim.gameObject.SetActive(false);
            return dim;
        }

        private static RectTransform Box(RectTransform panel) => (RectTransform)panel.Find("Box");

        private static void Title(RectTransform box, string text, float y, int size = 54)
        {
            RectTransform rt = UIKit.Rect(box, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(0f, 80f));
            UIKit.Label(rt, "Text", text, size, UIKit.Ink, TextAnchor.MiddleCenter, true);
        }

        private static VerticalLayoutGroup Buttons(RectTransform box, float top)
        {
            VerticalLayoutGroup column = UIKit.Column(box, "Buttons", 12f);
            ((RectTransform)column.transform).offsetMax = new Vector2(0f, -top);
            return column;
        }

        private void BuildPause()
        {
            pausePanel = Panel("Pause", new Vector2(560f, 620f));
            RectTransform box = Box(pausePanel);
            Title(box, "PAUSED", -30f);
            Transform col = Buttons(box, 140f).transform;
            resumeButton = UIKit.Button(col, "Resume", () => flow.Resume());
            checkpointButton = UIKit.Button(col, "Restart from checkpoint", () => flow.RestartFromCheckpoint());
            UIKit.Button(col, "Restart run", () => flow.RestartRun());
            UIKit.Button(col, "Settings", OpenSettings);
            UIKit.Button(col, "Main menu", () => flow.ReturnToMenu());
            UIKit.Button(col, "Quit", () => flow.QuitGame());
        }

        private void BuildSettings()
        {
            settingsPanel = Panel("Settings", new Vector2(700f, 560f), 0.95f);
            RectTransform box = Box(settingsPanel);
            Title(box, "SETTINGS", -26f, 46);
            Transform col = Buttons(box, 130f).transform;
            sensitivitySlider = UIKit.Slider(col, "Mouse sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 0.1f,
                v => Change(s => s.MouseSensitivity = v));
            invertToggle = UIKit.Toggle(col, "Invert mouse Y", false, v => Change(s => s.InvertY = v));
            volumeSlider = UIKit.Slider(col, "Volume", 0f, 1f, 0.85f, v => Change(s => s.MasterVolume = v));
            brightnessSlider = UIKit.Slider(col, "Brightness", -1f, 1f, 0f, v => Change(s => s.Brightness = v));
            bobToggle = UIKit.Toggle(col, "Head bob", true, v => Change(s => s.HeadBob = v));
            settingsBack = UIKit.Button(col, "Back", CloseSettings, 300f);
        }

        private void BuildNote()
        {
            notePanel = UIKit.Rect(canvas.transform, "Note", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 560f));
            Image paper = notePanel.gameObject.AddComponent<Image>();
            paper.sprite = UIKit.Square;
            paper.color = new Color(0.78f, 0.72f, 0.58f, 0.97f);
            RectTransform title = UIKit.Rect(notePanel, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(-80f, 60f));
            noteTitle = UIKit.Label(title, "Text", string.Empty, 32, new Color(0.15f, 0.1f, 0.08f), TextAnchor.MiddleCenter, true);
            RectTransform body = UIKit.Rect(notePanel, "Body", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(-100f, -170f));
            noteBody = UIKit.Label(body, "Text", string.Empty, 26, new Color(0.12f, 0.1f, 0.12f), TextAnchor.UpperLeft, true);
            noteBody.fontStyle = FontStyle.Italic;
            RectTransform close = UIKit.Rect(notePanel, "Close", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(0f, 40f));
            UIKit.Label(close, "Text", "[E] close", 20, new Color(0.3f, 0.25f, 0.2f), TextAnchor.MiddleCenter);
            notePanel.gameObject.SetActive(false);
        }

        private void BuildDeath()
        {
            deathPanel = UIKit.Fill(canvas.transform, "Death");
            RectTransform t = UIKit.Rect(deathPanel, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(1400f, 100f));
            Text title = UIKit.Label(t, "Text", "THE HOLLOW TOOK YOU", 72, new Color(0.8f, 0.12f, 0.1f), TextAnchor.MiddleCenter, true);
            UIKit.Soften(title);
            RectTransform s = UIKit.Rect(deathPanel, "Sub", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(1200f, 40f));
            deathSub = UIKit.Label(s, "Text", string.Empty, 24, UIKit.Dim, TextAnchor.MiddleCenter);
            RectTransform col = UIKit.Rect(deathPanel, "Buttons", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 40f), new Vector2(400f, 220f));
            VerticalLayoutGroup v = UIKit.Column(col, "Column", 12f);
            deathContinue = UIKit.Button(v.transform, "Continue from checkpoint", () => flow.RestartFromCheckpoint());
            UIKit.Button(v.transform, "Restart run", () => flow.RestartRun());
            UIKit.Button(v.transform, "Main menu", () => flow.ReturnToMenu());
            deathPanel.gameObject.SetActive(false);
        }

        private void BuildEnding()
        {
            subtitlePanel = UIKit.Rect(canvas.transform, "Subtitles", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(1500f, 110f));
            subtitle = UIKit.Label(subtitlePanel, "Text", string.Empty, 32, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Soften(subtitle);

            endPanel = UIKit.Fill(canvas.transform, "EndCard");
            RectTransform t = UIKit.Rect(endPanel, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1400f, 120f));
            UIKit.Label(t, "Text", "THE PRESENCE", 96, UIKit.Ink, TextAnchor.MiddleCenter, true);
            RectTransform s = UIKit.Rect(endPanel, "Sub", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1400f, 50f));
            UIKit.Label(s, "Text", "You escaped Blackthorn House.", 32, UIKit.Accent, TextAnchor.MiddleCenter, true);
            RectTransform st = UIKit.Rect(endPanel, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1400f, 60f));
            endStats = UIKit.Label(st, "Text", string.Empty, 22, UIKit.Dim, TextAnchor.MiddleCenter);
            RectTransform col = UIKit.Rect(endPanel, "Buttons", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 20f), new Vector2(400f, 220f));
            VerticalLayoutGroup v = UIKit.Column(col, "Column", 12f);
            endAgain = UIKit.Button(v.transform, "Play again", () => flow.RestartRun());
            UIKit.Button(v.transform, "Main menu", () => flow.ReturnToMenu());
            UIKit.Button(v.transform, "Quit", () => flow.QuitGame());
            endPanel.gameObject.SetActive(false);
        }

        private void BuildDev()
        {
            devPanel = UIKit.Rect(canvas.transform, "Dev", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -80f), new Vector2(720f, 440f));
            Image bg = devPanel.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            bg.color = new Color(0f, 0f, 0f, 0.7f);
            RectTransform inner = UIKit.Rect(devPanel, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-28f, -24f));
            devText = UIKit.Label(inner, "Label", string.Empty, 17, new Color(0.7f, 0.95f, 0.75f), TextAnchor.UpperLeft);
            devText.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 17) ?? UIKit.BodyFont;
            devPanel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ events

        private void OnMessage(string text)
        {
            message.text = text;
        }

        private void OnHint(string text)
        {
            hint.text = text;
            hintUntil = Time.unscaledTime + HintSeconds;
        }

        private void OnObjective(ObjectiveStage stage)
        {
            objectiveFlash = 1f;
            if (stage != ObjectiveStage.RestoringPower)
                audioPlayer?.Play2D(AudioCue.ObjectiveUpdate);
        }

        private void OnNote(ReadableNote note)
        {
            if (openNote == note && notePanel.gameObject.activeSelf)
            {
                CloseNote();
                return;
            }
            openNote = note;
            noteTitle.text = note.Title;
            noteBody.text = note.Body;
            noteOpenedAt = interactor.transform.position;
            noteOpenedTime = Time.unscaledTime;
            notePanel.gameObject.SetActive(true);
        }

        private void CloseNote()
        {
            openNote = null;
            notePanel.gameObject.SetActive(false);
        }

        private void OnDeath()
        {
            deathStartedAt = Time.unscaledTime;
            CloseNote();
        }

        private void OpenSettings()
        {
            GameSettings s = Settings ?? new GameSettings();
            sensitivitySlider.SetValueWithoutNotify(s.MouseSensitivity);
            invertToggle.SetIsOnWithoutNotify(s.InvertY);
            volumeSlider.SetValueWithoutNotify(s.MasterVolume);
            brightnessSlider.SetValueWithoutNotify(s.Brightness);
            bobToggle.SetIsOnWithoutNotify(s.HeadBob);
            settingsPanel.gameObject.SetActive(true);
            UIKit.Select(settingsBack);
        }

        private void CloseSettings()
        {
            settingsPanel.gameObject.SetActive(false);
            Settings?.Save();
            UIKit.Select(resumeButton);
        }

        private void Change(Action<GameSettings> apply)
        {
            GameSettings s = Settings;
            if (s == null)
                return;
            apply(s);
            SettingsChanged?.Invoke();
        }

        // ------------------------------------------------------------ frame

        private void Update()
        {
            if (flow == null)
                return;

            float now = Time.unscaledTime;
            GameFlow f = flow.Flow;
            RunState state = f.RunState;
            bool ended = ending != null && ending.Phase != EndingSequence.Stage.Idle;
            bool dead = state == RunState.Dead;

            if (!devInitialized && flow.Services != null)
            {
                devInitialized = true;
                devVisible = Settings != null && Settings.DevOverlay;
            }
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                devVisible = !devVisible;

            // Pause and settings.
            bool paused = state == RunState.Paused;
            if (paused != pausePanel.gameObject.activeSelf)
            {
                pausePanel.gameObject.SetActive(paused);
                if (paused)
                {
                    checkpointButton.interactable = flow.Session != null && flow.Session.HasCheckpoint;
                    UIKit.Select(resumeButton);
                    CloseNote();
                }
                else
                {
                    settingsPanel.gameObject.SetActive(false);
                }
            }

            // HUD.
            bool showHud = state == RunState.Playing && !ended;
            hud.gameObject.SetActive(showHud || paused);
            if (showHud || paused)
                UpdateHud(now, f);

            // Note.
            if (openNote != null)
            {
                bool moved = (interactor.transform.position - noteOpenedAt).sqrMagnitude > 1.4f;
                bool closeKey = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
                if (moved || state != RunState.Playing || closeKey || now - noteOpenedTime > 20f)
                    CloseNote();
            }

            // Death: red flash, fade to black, then the card.
            float fade = 0f;
            float red = 0f;
            if (dead && deathStartedAt > 0f)
            {
                float t = now - deathStartedAt;
                red = Mathf.Clamp01(1f - t / 1.2f) * 0.7f;
                fade = Mathf.Clamp01((t - 0.4f) / 1.8f) * 0.92f;
                bool showCard = t > 2.4f;
                if (showCard != deathPanel.gameObject.activeSelf)
                {
                    deathPanel.gameObject.SetActive(showCard);
                    if (showCard)
                    {
                        bool hasCheckpoint = flow.Session != null && flow.Session.HasCheckpoint;
                        deathContinue.interactable = hasCheckpoint;
                        deathSub.text = "Attempt " + (flow.Session?.Attempt ?? 1) + "  -  you will return to the fuse box, power on";
                        UIKit.Select(hasCheckpoint ? deathContinue : null);
                    }
                }
            }

            // Ending.
            if (ended)
            {
                fade = Mathf.Max(fade, ending.Fade);
                subtitle.text = ending.Subtitle;
                bool card = ending.Phase == EndingSequence.Stage.EndCard;
                if (card != endPanel.gameObject.activeSelf)
                {
                    endPanel.gameObject.SetActive(card);
                    if (card)
                    {
                        endStats.text = Stats();
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                        UIKit.Select(endAgain);
                    }
                }
                if (!card)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    Keyboard k = Keyboard.current;
                    if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
                        ending.Skip();
                }
            }
            else
            {
                subtitle.text = string.Empty;
            }

            // Fade in at the start of every attempt.
            float startFade = Mathf.Clamp01(1f - flow.RunSeconds / 1.5f);
            if (!flow.RunStarted)
                startFade = 1f;
            fader.color = new Color(0f, 0f, 0f, Mathf.Max(fade, startFade));
            flash.color = new Color(UIKit.Blood.r, UIKit.Blood.g, UIKit.Blood.b, red);

            if (devVisible != devPanel.gameObject.activeSelf)
                devPanel.gameObject.SetActive(devVisible);
            if (devVisible && now >= devRefreshAt)
            {
                devRefreshAt = now + 0.2f;
                devText.text = DevText();
            }
        }

        private void UpdateHud(float now, GameFlow f)
        {
            objective.text = f.ObjectiveText;
            objectiveFlash = Mathf.MoveTowards(objectiveFlash, 0f, Time.unscaledDeltaTime * 0.7f);
            objective.color = Color.Lerp(UIKit.Ink, UIKit.Accent, objectiveFlash);

            Interactable target = interactor != null ? interactor.CurrentTarget : null;
            prompt.text = target != null && openNote == null ? "[E]  " + target.Prompt : string.Empty;
            float size = target != null ? 18f : 8f;
            crosshair.rectTransform.sizeDelta = Vector2.Lerp(crosshair.rectTransform.sizeDelta, new Vector2(size, size), 0.3f);
            crosshair.color = target != null ? new Color(1f, 0.8f, 0.5f, 0.85f) : new Color(1f, 1f, 1f, 0.45f);

            float messageAge = now - flow.LastMessageTime;
            message.color = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, Mathf.Clamp01((MessageSeconds - messageAge) / 0.6f));
            hint.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, Mathf.Clamp01((hintUntil - now) / 0.8f));

            float controlsAlpha = Mathf.Clamp01((ControlsHintSeconds - flow.RunSeconds) / 2f);
            controls.color = new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, controlsAlpha);

            if (lantern != null)
            {
                lanternRing.fillAmount = lantern.Charge;
                lanternRing.color = lantern.Ready ? new Color(1f, 0.78f, 0.45f, 0.95f) : new Color(0.7f, 0.5f, 0.3f, 0.7f);
                lanternKey.color = lantern.Ready ? UIKit.Ink : UIKit.Dim;
            }

            IPhysicalInputSource sensors = flow.Services?.Sensors;
            if (sensors != null)
            {
                sensorChip.text = sensors.Condition == RunCondition.VIRTUAL_ONLY
                    ? "SENSORS  OFF (virtual only)"
                    : "SENSORS  " + (sensors.Condition == RunCondition.SIMULATED ? "SIMULATED" : "UDP") + "  -  " + sensors.Status;
            }
        }

        private string Stats()
        {
            float total = flow.TotalRunSeconds;
            int minutes = (int)(total / 60f);
            int seconds = (int)(total % 60f);
            return string.Format(CultureInfo.InvariantCulture, "Time {0}:{1:00}     Deaths {2}     Lantern {3}/{4}     Sensors {5}",
                minutes, seconds, flow.Session?.Deaths ?? 0, lantern != null ? lantern.Hits : 0, lantern != null ? lantern.Uses : 0,
                flow.Services?.Sensors?.Condition.ToString() ?? "-");
        }

        private string DevText()
        {
            StringBuilder b = devBuilder;
            b.Clear();
            IPhysicalInputSource sensors = flow.Services?.Sensors;
            b.Append("DEV  (F1)\n");
            b.Append("condition  ").Append(sensors?.Condition).Append("   source ").Append(sensors?.Status)
                .Append(sensors != null && sensors.PhysicalInputMasked ? "  [masked]" : string.Empty).Append('\n');
            if (flow.World != null)
            {
                var w = flow.World.GetCurrentWorldState();
                b.Append("physical   ").Append(w.physical.presence).Append('/').Append(w.physical.confidence)
                    .Append(" dist=").Append(w.physical.disturbance).Append(" evt=").Append(w.physical.causeEventId).Append('\n');
                b.Append("player     d=").Append(w.player.distanceToGhost.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" zone=").Append(w.player.inThreatZone).Append(" band=").Append(w.player.inWarnBand).Append('\n');
                b.Append("virtual    threat=").Append(w.virtualWorld.threatLevel.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" beat=").Append(w.virtualWorld.mandatoryWarningBeat).Append(" commit=").Append(w.virtualWorld.encounterCommitted)
                    .Append(" grace=").Append(w.virtualWorld.attackGraceActive).Append('\n');
            }
            if (flow.GhostRuntime != null && flow.GhostRuntime.IsInitialized)
            {
                b.Append("decision   #").Append(flow.GhostRuntime.CurrentDecisionId).Append(' ').Append(flow.GhostRuntime.LastAction)
                    .Append(" [").Append(flow.GhostRuntime.LastRuleId).Append("] -> ").Append(flow.GhostRuntime.CurrentState).Append('\n');
                b.Append("reason     ").Append(flow.GhostRuntime.LastReason).Append('\n');
            }
            if (flow.Ghost != null)
            {
                b.Append("ghost      phase=").Append(flow.Ghost.Phase).Append(" cd=").Append(flow.Ghost.CooldownMs)
                    .Append(" dormant=").Append(flow.Ghost.Dormant).Append(" nav=").Append(flow.Ghost.HasNavMesh).Append('\n');
            }
            b.Append("flow       ").Append(flow.Flow.Stage).Append(' ').Append(flow.Flow.RunState)
                .Append(" t=").Append(flow.RunSeconds.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" attempt=").Append(flow.Session?.Attempt).Append('\n');
            if (sensors != null && sensors.Condition == RunCondition.SIMULATED)
                b.Append("\nsim keys   1/2/3 presence  4 confidence  5 disturbance\n           6 resend event  7 stale line  8 reboot  9 stop/resume");
            return b.ToString();
        }
    }
}
