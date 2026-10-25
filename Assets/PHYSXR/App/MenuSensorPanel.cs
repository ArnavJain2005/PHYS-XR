using System.Collections.Generic;
using PHYSXR.Game.Flow;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PHYSXR.App
{
    // The welcome screen's sensor panel: draws a SensorPanelModel.View (built
    // from the real diagnostics) as a compact, translucent panel in its own
    // column, with its own nested canvas so 4 Hz updates never rebuild the
    // menu. Every state has a shape AND a word (filled dot OK, ring notice,
    // cross fault, dash no data, turning ring busy), so colour is never the
    // only cue. Text is only touched when it changes; a state change pulses
    // that one indicator instead of flashing the panel.
    //
    // "Setup steps" swaps the readings for the bring-up checklist. Nothing
    // here derives or influences a ghost decision.
    public class MenuSensorPanel : MonoBehaviour
    {
        public static readonly Color OkColor = new Color(0.52f, 0.82f, 0.56f);
        public static readonly Color NoticeColor = new Color(0.98f, 0.72f, 0.32f);
        public static readonly Color FaultColor = new Color(0.96f, 0.4f, 0.34f);
        public static readonly Color OffColor = new Color(0.56f, 0.56f, 0.6f);
        public static readonly Color BusyColor = new Color(0.62f, 0.76f, 0.96f);

        private const string SetupSteps =
            "1   Plug the ESP32 in by USB and close the Arduino Serial Monitor.\n"
            + "2   Keep the station still for 2 s after power-up (IMU calibration).\n"
            + "3   Port AUTO finds the station, or pick its COM port: Device Manager >\n"
            + "     Ports (COM & LPT), e.g. Silicon Labs CP210x (COM6).\n"
            + "4   Begin once the link says CONNECTED and the readings move.\n\n"
            + "Hardware checks before first power: Docs/HARDWARE_ACCEPTANCE.md.";

        private sealed class Indicator
        {
            public Image Icon;
            public Text Word;
            public SensorPanelModel.Mark Mark = (SensorPanelModel.Mark)(-1);
            public string LastWord;
            public float Pulse;
        }

        private sealed class ReadoutRow
        {
            public GameObject Root;
            public Text Name, Part, Value, Detail;
            public Indicator State;
        }

        private sealed class FieldRow
        {
            public GameObject Root;
            public Text Label, Value;
        }

        private RectTransform body;
        private Text heading, source, explanation, pins, message, helpText, helpButtonLabel;
        private Indicator chip, messageMark;
        private GameObject stationHeader, readingsHeader, pinsHeader, pinsRow, messageBox, divider, footerRow;
        private LayoutElement messageLayout, explanationLayout;
        private Image messageBg;
        private readonly List<FieldRow> fields = new List<FieldRow>();
        private readonly List<ReadoutRow> readouts = new List<ReadoutRow>();
        private readonly List<Indicator> indicators = new List<Indicator>();
        private bool helpOpen;
        private int compact;
        private SensorPanelModel.View last;

        private static Sprite disc, ring, cross, dash;

        public CanvasGroup Group { get; private set; }

        public void Build(RectTransform panel)
        {
            Canvas nested = panel.gameObject.AddComponent<Canvas>();
            nested.overrideSorting = false;
            panel.gameObject.AddComponent<GraphicRaycaster>();
            Group = panel.gameObject.AddComponent<CanvasGroup>();
            panel.gameObject.AddComponent<RectMask2D>();

            Image bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = UIKit.Square;
            bg.color = new Color(0.02f, 0.022f, 0.03f, 0.82f);
            bg.raycastTarget = false;
            MenuArt.AddFrame(panel, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.3f));

            body = UIKit.Fill(panel, "Body");
            body.offsetMin = new Vector2(30f, 22f);
            body.offsetMax = new Vector2(-30f, -24f);
            VerticalLayoutGroup v = body.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;

            // Header: title left, link chip right.
            RectTransform head = Row("Header", 44f);
            heading = UIKit.Label(head, "Title", "PHYSICAL SENSORS", 30, UIKit.Ink, TextAnchor.MiddleLeft, true);
            heading.horizontalOverflow = HorizontalWrapMode.Overflow;
            chip = NewIndicator(head, TextAnchor.MiddleRight, 18, true);

            source = UIKit.Label(Row("Source", 28f), "Text", string.Empty, 19, UIKit.Dim, TextAnchor.MiddleLeft);
            source.horizontalOverflow = HorizontalWrapMode.Overflow;

            divider = Row("Divider", 10f).gameObject;
            Image line = UIKit.Image(divider.transform, "Line", new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.28f));
            line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1.5f);

            stationHeader = Section("STATION");
            for (int i = 0; i < 4; i++)
                fields.Add(NewField());

            readingsHeader = Section("SENSOR READINGS");
            for (int i = 0; i < 3; i++)
                readouts.Add(NewReadout());

            pinsHeader = Section("PINS REPORTED BY THE STATION");
            RectTransform pr = Row("Pins", 26f);
            pinsRow = pr.gameObject;
            pins = UIKit.Label(pr, "Text", string.Empty, 19, UIKit.Ink, TextAnchor.MiddleLeft);

            explanation = UIKit.Label(body, "Explanation", string.Empty, 20, UIKit.Ink, TextAnchor.UpperLeft);
            explanation.lineSpacing = 1.15f;
            explanationLayout = explanation.gameObject.AddComponent<LayoutElement>();

            helpText = UIKit.Label(body, "Steps", SetupSteps, 19, UIKit.Ink, TextAnchor.UpperLeft);
            helpText.lineSpacing = 1.2f;
            helpText.gameObject.AddComponent<LayoutElement>().preferredHeight = 230f;
            helpText.gameObject.SetActive(false);

            RectTransform spacer = UIKit.Rect(body, "Spacer", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            // Message box: the most important problem, in words.
            RectTransform mb = Row("Message", 48f);
            messageBox = mb.gameObject;
            messageLayout = mb.GetComponent<LayoutElement>();
            messageBg = mb.gameObject.AddComponent<Image>();
            messageBg.sprite = UIKit.Square;
            messageBg.raycastTarget = false;
            messageMark = NewIndicator(mb, TextAnchor.UpperLeft, 0, false);
            ((RectTransform)messageMark.Icon.transform).anchoredPosition = new Vector2(14f, -14f);
            message = UIKit.Label(mb, "Text", string.Empty, 19, UIKit.Ink, TextAnchor.UpperLeft);
            ((RectTransform)message.transform).offsetMin = new Vector2(36f, 8f);
            ((RectTransform)message.transform).offsetMax = new Vector2(-12f, -8f);
            message.verticalOverflow = VerticalWrapMode.Overflow;

            // Setup steps toggle.
            RectTransform footer = Row("Footer", 30f);
            footerRow = footer.gameObject;
            RectTransform hb = UIKit.Rect(footer, "SetupSteps", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f, 30f));
            Image hbg = hb.gameObject.AddComponent<Image>();
            hbg.sprite = UIKit.Square;
            hbg.color = new Color(1f, 1f, 1f, 0f);
            Button helpButton = hb.gameObject.AddComponent<Button>();
            helpButton.transition = Selectable.Transition.None;
            helpButtonLabel = UIKit.Label(hb, "Label", "Setup steps  >", 19, UIKit.Accent, TextAnchor.MiddleLeft);
            helpButton.onClick.AddListener(ToggleHelp);
            Navigation nav = helpButton.navigation;
            nav.mode = Navigation.Mode.None;
            helpButton.navigation = nav;
        }

        // 0 full; 1 compact (no line counters or pins); 2 tight (no station
        // section either: the chip and source line still name the link).
        public void SetCompact(int value)
        {
            if (compact == value)
                return;
            compact = value;
            last = null;
        }

        private void ToggleHelp()
        {
            helpOpen = !helpOpen;
            helpButtonLabel.text = helpOpen ? "<  Live status" : "Setup steps  >";
            last = null;
        }

        // ------------------------------------------------------------ render

        public void Render(SensorPanelModel.View v)
        {
            bool live = v.ShowsLiveReadings && !helpOpen;
            bool structure = last == null || last.Mode != v.Mode;

            Set(heading, v.Heading);
            Set(source, v.Source);
            SetIndicator(chip, v.LinkMark, v.LinkWord);

            stationHeader.SetActive(live && compact < 2);
            for (int i = 0; i < fields.Count; i++)
            {
                bool show = live && i < v.Station.Count && (compact == 0 || (compact == 1 && i < 2));
                fields[i].Root.SetActive(show);
                if (show)
                {
                    Set(fields[i].Label, v.Station[i].Label);
                    Set(fields[i].Value, v.Station[i].Value);
                }
            }

            readingsHeader.SetActive(live);
            for (int i = 0; i < readouts.Count; i++)
            {
                bool show = live && i < v.Readings.Count;
                readouts[i].Root.SetActive(show);
                if (!show)
                    continue;
                SensorPanelModel.Readout r = v.Readings[i];
                ReadoutRow row = readouts[i];
                Set(row.Name, r.Name);
                Set(row.Part, r.Part);
                Set(row.Value, r.Value);
                Set(row.Detail, r.Detail);
                row.Value.color = r.Mark == SensorPanelModel.Mark.Ok ? UIKit.Ink
                    : r.Mark == SensorPanelModel.Mark.Fault ? Color.Lerp(UIKit.Ink, FaultColor, 0.55f) : UIKit.Dim;
                SetIndicator(row.State, r.Mark, r.State);
            }

            bool showPins = live && v.Pins.Count > 0 && compact == 0;
            pinsHeader.SetActive(showPins);
            pinsRow.SetActive(showPins);
            if (showPins)
            {
                var sb = new System.Text.StringBuilder();
                foreach (SensorPanelModel.Pin p in v.Pins)
                    sb.Append(p.Name).Append(' ').Append(p.Value).Append("    ");
                Set(pins, sb.ToString().TrimEnd());
            }

            bool explain = !v.ShowsLiveReadings && !helpOpen;
            explanation.gameObject.SetActive(explain);
            if (explain && Set(explanation, v.Explanation))
                explanationLayout.preferredHeight = -1f;
            helpText.gameObject.SetActive(helpOpen);

            bool hasMessage = !string.IsNullOrEmpty(v.Message) && !helpOpen;
            messageBox.SetActive(hasMessage);
            if (hasMessage)
            {
                if (Set(message, v.Message))
                {
                    Canvas.ForceUpdateCanvases();
                    messageLayout.preferredHeight = Mathf.Max(44f, message.preferredHeight + 18f);
                }
                SetIndicator(messageMark, v.MessageMark, null);
                Color tint = v.MessageMark == SensorPanelModel.Mark.Fault ? FaultColor : NoticeColor;
                messageBg.color = new Color(tint.r * 0.25f, tint.g * 0.22f, tint.b * 0.2f, 0.55f);
            }

            footerRow.SetActive(v.Mode == SensorPanelModel.Mode.UsbSerial);

            LayoutRebuilder.ForceRebuildLayoutImmediate(body);
            last = v;
        }

        // Height the content needs, including the body's padding.
        public float ContentHeight => LayoutUtility.GetPreferredHeight(body) + 46f;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (Indicator ind in indicators)
            {
                if (ind.Pulse > 0f)
                {
                    ind.Pulse = Mathf.Max(0f, ind.Pulse - dt * 2.2f);
                    float s = 1f + ind.Pulse * 0.7f;
                    ind.Icon.rectTransform.localScale = new Vector3(s, s, 1f);
                    if (ind.Word != null)
                        ind.Word.color = Color.Lerp(MarkColor(ind.Mark), Color.white, ind.Pulse * 0.6f);
                }
                if (ind.Mark == SensorPanelModel.Mark.Busy)
                    ind.Icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 240f);
                else if (ind.Icon.rectTransform.localRotation != Quaternion.identity)
                    ind.Icon.rectTransform.localRotation = Quaternion.identity;
            }
        }

        // ------------------------------------------------------------ pieces

        private RectTransform Row(string name, float height)
        {
            RectTransform r = UIKit.Rect(body, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            LayoutElement le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return r;
        }

        private GameObject Section(string title)
        {
            RectTransform r = Row("Section_" + title, 30f);
            Text t = UIKit.Label(r, "Text", title, 17, UIKit.Accent, TextAnchor.LowerLeft);
            t.fontStyle = FontStyle.Bold;
            return r.gameObject;
        }

        private FieldRow NewField()
        {
            RectTransform r = Row("Field", 28f);
            var f = new FieldRow { Root = r.gameObject };
            f.Label = UIKit.Label(r, "Label", string.Empty, 19, UIKit.Dim, TextAnchor.MiddleLeft);
            ((RectTransform)f.Label.transform).anchorMax = new Vector2(0f, 1f);
            ((RectTransform)f.Label.transform).sizeDelta = new Vector2(130f, 0f);
            ((RectTransform)f.Label.transform).pivot = new Vector2(0f, 0.5f);
            f.Value = UIKit.Label(r, "Value", string.Empty, 20, UIKit.Ink, TextAnchor.MiddleLeft);
            ((RectTransform)f.Value.transform).offsetMin = new Vector2(130f, 0f);
            f.Value.horizontalOverflow = HorizontalWrapMode.Wrap;
            f.Value.verticalOverflow = VerticalWrapMode.Truncate;
            return f;
        }

        private ReadoutRow NewReadout()
        {
            RectTransform r = Row("Readout", 66f);
            var row = new ReadoutRow { Root = r.gameObject };

            RectTransform top = UIKit.Rect(r, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 32f));
            row.Name = UIKit.Label(top, "Name", string.Empty, 22, UIKit.Ink, TextAnchor.MiddleLeft);
            ((RectTransform)row.Name.transform).offsetMin = new Vector2(26f, 0f);
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            row.Part = UIKit.Label(top, "Part", string.Empty, 16, UIKit.Dim, TextAnchor.MiddleLeft);
            ((RectTransform)row.Part.transform).offsetMin = new Vector2(96f, 0f);
            row.Part.horizontalOverflow = HorizontalWrapMode.Overflow;
            row.Value = UIKit.Label(top, "Value", string.Empty, 23, UIKit.Ink, TextAnchor.MiddleRight);
            ((RectTransform)row.Value.transform).offsetMin = new Vector2(190f, 0f);

            RectTransform bottom = UIKit.Rect(r, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(0f, 26f));
            row.State = NewIndicator(top, TextAnchor.MiddleLeft, 0, false);
            ((RectTransform)row.State.Icon.transform).anchoredPosition = new Vector2(8f, 0f);
            Text word = UIKit.Label(bottom, "State", string.Empty, 17, UIKit.Ink, TextAnchor.MiddleLeft);
            word.fontStyle = FontStyle.Bold;
            ((RectTransform)word.transform).offsetMin = new Vector2(26f, 0f);
            ((RectTransform)word.transform).anchorMax = new Vector2(0f, 1f);
            ((RectTransform)word.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)word.transform).sizeDelta = new Vector2(150f, 0f);
            row.State.Word = word;
            row.Detail = UIKit.Label(bottom, "Detail", string.Empty, 18, UIKit.Dim, TextAnchor.MiddleLeft);
            ((RectTransform)row.Detail.transform).offsetMin = new Vector2(180f, 0f);
            row.Detail.verticalOverflow = VerticalWrapMode.Truncate;
            return row;
        }

        // An icon with an optional word. For chips (header), the word is
        // right-aligned and the icon sits just left of it.
        private Indicator NewIndicator(RectTransform parent, TextAnchor wordAnchor, int wordSize, bool chipStyle)
        {
            var ind = new Indicator();
            RectTransform ir = UIKit.Rect(parent, "Mark", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 15f));
            if (chipStyle)
            {
                ir.anchorMin = ir.anchorMax = new Vector2(1f, 0.5f);
            }
            else if (wordAnchor == TextAnchor.UpperLeft)
            {
                ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
            }
            ind.Icon = ir.gameObject.AddComponent<Image>();
            ind.Icon.raycastTarget = false;
            if (wordSize > 0)
            {
                ind.Word = UIKit.Label(parent, "Word", string.Empty, wordSize, UIKit.Ink, wordAnchor);
                ind.Word.fontStyle = FontStyle.Bold;
                ind.Word.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            indicators.Add(ind);
            return ind;
        }

        private void SetIndicator(Indicator ind, SensorPanelModel.Mark mark, string word)
        {
            bool changed = ind.Mark != mark || (word != null && ind.LastWord != word);
            if (!changed)
                return;
            bool first = (int)ind.Mark < 0;
            ind.Mark = mark;
            ind.LastWord = word;
            ind.Icon.sprite = MarkSprite(mark);
            ind.Icon.color = MarkColor(mark);
            if (ind.Word != null && word != null)
            {
                ind.Word.text = word;
                ind.Word.color = MarkColor(mark);
                if (ind == chip)
                {
                    float w = ind.Word.preferredWidth;
                    ((RectTransform)ind.Icon.transform).anchoredPosition = new Vector2(-w - 14f, 0f);
                }
            }
            if (!first)
                ind.Pulse = 1f;
        }

        private static bool Set(Text t, string value)
        {
            value = value ?? string.Empty;
            if (t.text == value)
                return false;
            t.text = value;
            return true;
        }

        public static Color MarkColor(SensorPanelModel.Mark m)
        {
            switch (m)
            {
                case SensorPanelModel.Mark.Ok: return OkColor;
                case SensorPanelModel.Mark.Notice: return NoticeColor;
                case SensorPanelModel.Mark.Fault: return FaultColor;
                case SensorPanelModel.Mark.Busy: return BusyColor;
                default: return OffColor;
            }
        }

        private static Sprite MarkSprite(SensorPanelModel.Mark m)
        {
            switch (m)
            {
                case SensorPanelModel.Mark.Ok: return disc ?? (disc = MakeSprite((x, y) => Edge(0.85f - Mathf.Sqrt(x * x + y * y))));
                case SensorPanelModel.Mark.Fault:
                    return cross ?? (cross = MakeSprite((x, y) => Edge(0.2f - Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y)) * 0.7071f) * Edge(0.9f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)))));
                case SensorPanelModel.Mark.Off: return dash ?? (dash = MakeSprite((x, y) => Edge(0.17f - Mathf.Abs(y)) * Edge(0.8f - Mathf.Abs(x))));
                case SensorPanelModel.Mark.Busy:
                    return ring ?? (ring = MakeSprite((x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Edge(0.85f - r) * Edge(r - 0.55f) * (x > 0.15f && y > 0.15f ? 0f : 1f); }));
                default:
                    return MakeRingNotice();
            }
        }

        private static Sprite noticeRing;

        private static Sprite MakeRingNotice()
        {
            return noticeRing ?? (noticeRing = MakeSprite((x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Max(Edge(0.88f - r) * Edge(r - 0.6f), Edge(0.26f - r)); }));
        }

        private static float Edge(float d) => Mathf.Clamp01(d * 16f + 0.5f);

        private static Sprite MakeSprite(System.Func<float, float, float> alpha)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            s.hideFlags = HideFlags.DontSave;
            return s;
        }
    }
}
