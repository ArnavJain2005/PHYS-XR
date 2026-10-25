using System;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PHYSXR.App
{
    // Hover, keyboard/gamepad focus and press feedback for a main-menu
    // control: the background warms, a thin amber frame and an underline
    // reveal from the centre, the label brightens, and a press gives a brief
    // flash. The same look applies whether the pointer or the keyboard
    // selects the control. Unscaled time, so it works on any time scale.
    public class MenuButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
        IPointerDownHandler
    {
        // Programmatic selection (opening a panel) does not tick.
        public static float SilentUntil;

        public Image Background;
        public Image Frame;
        public Text Label;
        public RectTransform Underline;
        public Graphic Chevron;
        public bool Primary;
        public Action Hovered;

        private Selectable selectable;
        private bool pointer, selected;
        private float level, press;

        private static readonly Color Idle = new Color(0.035f, 0.035f, 0.045f, 0.62f);
        private static readonly Color Warm = new Color(0.17f, 0.11f, 0.055f, 0.86f);
        private static readonly Color PrimaryIdle = new Color(0.12f, 0.075f, 0.035f, 0.78f);

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            pointer = true;
            Tick();
        }

        public void OnPointerExit(PointerEventData e)
        {
            pointer = false;
        }

        public void OnSelect(BaseEventData e)
        {
            selected = true;
            if (!(e is PointerEventData))
                Tick();
        }

        public void OnDeselect(BaseEventData e)
        {
            selected = false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (selectable == null || selectable.interactable)
                press = 1f;
        }

        private void Tick()
        {
            if (Time.unscaledTime < SilentUntil || (selectable != null && !selectable.IsInteractable()))
                return;
            Hovered?.Invoke();
        }

        private void OnDisable()
        {
            pointer = false;
            level = 0f;
            press = 0f;
            Apply();
        }

        private void Update()
        {
            bool active = (pointer || selected) && (selectable == null || selectable.IsInteractable());
            level = Mathf.MoveTowards(level, active ? 1f : 0f, Time.unscaledDeltaTime * (active ? 7f : 4f));
            press = Mathf.MoveTowards(press, 0f, Time.unscaledDeltaTime * 4f);
            Apply();
        }

        private void Apply()
        {
            float k = level * level * (3f - 2f * level);
            if (Background != null)
            {
                Color c = Color.Lerp(Primary ? PrimaryIdle : Idle, Warm, k);
                c += new Color(0.25f, 0.16f, 0.07f, 0f) * press;
                Background.color = c;
            }
            if (Frame != null)
                Frame.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, (Primary ? 0.55f : 0.12f) + k * 0.45f);
            if (Label != null)
                Label.color = Color.Lerp(UIKit.Ink * 0.9f + new Color(0f, 0f, 0f, 0.1f), new Color(1f, 0.93f, 0.8f), k);
            if (Underline != null)
                Underline.localScale = new Vector3(Mathf.Max(0.0001f, k), 1f, 1f);
            if (Chevron != null)
                Chevron.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, (Primary ? 0.6f : 0f) + k * 0.4f);
        }
    }
}
