using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Player;
using UnityEngine;

namespace PHYSXR.Game.UI
{
    // Placeholder desktop HUD and menus, drawn with IMGUI so the greybox needs
    // no Canvas, font or prefab assets. It only reads state and calls
    // GameFlowController; it owns no game rules. A world-space uGUI version
    // is needed for Quest (M8) and the final presentation (M10).
    //
    //   HUD:     objective, crosshair, targeted-object prompt, brief messages.
    //   Pause:   Resume / Restart run / Quit.
    //   Success: completion time, Restart run / Quit.
    public class GameUI : MonoBehaviour
    {
        private const float MessageSeconds = 3f;
        private const float ControlsHintSeconds = 10f;

        private GameFlowController flowController;
        private PlayerInteractor interactor;
        private GUIStyle label;
        private GUIStyle centered;
        private GUIStyle title;
        private Texture2D panel;

        public void Initialize(GameFlowController flowController, PlayerInteractor interactor)
        {
            this.flowController = flowController;
            this.interactor = interactor;
        }

        private void OnGUI()
        {
            if (flowController == null)
                return;

            EnsureStyles();
            GameFlow flow = flowController.Flow;
            float w = Screen.width;
            float h = Screen.height;

            if (flow.RunState == RunState.Succeeded)
            {
                DrawSuccess(w, h);
                return;
            }

            GUI.Label(new Rect(20, 16, w - 40, 30), "Objective: " + flow.ObjectiveText, label);

            if (flowController.RunSeconds < ControlsHintSeconds && flow.RunState == RunState.Playing)
                GUI.Label(new Rect(20, 44, w - 40, 26), "WASD move   Mouse look   E interact   Esc pause", label);

            if (flow.RunState == RunState.Playing)
            {
                GUI.Label(new Rect(w / 2 - 10, h / 2 - 14, 20, 28), "+", centered);

                Interactable target = interactor != null ? interactor.CurrentTarget : null;
                if (target != null)
                    GUI.Label(new Rect(0, h / 2 + 30, w, 30), "[E] " + target.Prompt, centered);
            }

            if (Time.unscaledTime - flowController.LastMessageTime < MessageSeconds)
                GUI.Label(new Rect(0, h * 0.72f, w, 30), flowController.LastMessage, centered);

            if (flow.RunState == RunState.Paused)
                DrawPause(w, h);
        }

        private void DrawPause(float w, float h)
        {
            Rect box = new Rect(w / 2 - 150, h / 2 - 110, 300, 220);
            GUI.DrawTexture(box, panel);
            GUI.Label(new Rect(box.x, box.y + 14, box.width, 34), "Paused", title);

            if (GUI.Button(new Rect(box.x + 50, box.y + 64, 200, 36), "Resume"))
                flowController.Resume();
            if (GUI.Button(new Rect(box.x + 50, box.y + 110, 200, 36), "Restart run"))
                flowController.RestartRun();
            if (GUI.Button(new Rect(box.x + 50, box.y + 156, 200, 36), "Quit"))
                flowController.QuitGame();
        }

        private void DrawSuccess(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), panel);
            GUI.Label(new Rect(0, h / 2 - 90, w, 40), "You escaped Blackthorn House", title);
            GUI.Label(new Rect(0, h / 2 - 40, w, 30),
                "Run time " + flowController.RunSeconds.ToString("F1") + " s   (placeholder ending)", centered);

            if (GUI.Button(new Rect(w / 2 - 100, h / 2 + 10, 200, 36), "Restart run"))
                flowController.RestartRun();
            if (GUI.Button(new Rect(w / 2 - 100, h / 2 + 56, 200, 36), "Quit"))
                flowController.QuitGame();
        }

        private void EnsureStyles()
        {
            if (label != null)
                return;

            label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            label.normal.textColor = new Color(0.9f, 0.9f, 0.85f);
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(centered) { fontSize = 28, fontStyle = FontStyle.Bold };

            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            panel.Apply();
        }

        private void OnDestroy()
        {
            if (panel != null)
                Destroy(panel);
        }
    }
}
