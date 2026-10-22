using System.Collections.Generic;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Gameplay;
using PHYSXR.Ghost;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Lights struggle near The Hollow: any flickering light (fixtures,
    // emergency lamps, candles) within a few metres of the visible ghost sags
    // and stutters, more the closer it is, and recovers when it leaves. It
    // often gives the ghost away before the figure itself is seen.
    //
    // Hysteresis keeps a light from chattering at the edge: it is disturbed
    // from 4.5 m and released only beyond 6 m. Each newly disturbed light
    // may crackle once (at most once per light every 8 s, and once every
    // 2.5 s overall). Checked ten times a second. Presentation only: it reads
    // the ghost's position and visibility.
    public class PresenceLights : MonoBehaviour
    {
        private const float Enter = 4.5f, Exit = 6f, Full = 1.6f;
        private const float LightCooldown = 8f, GlobalCooldown = 2.5f;

        private readonly List<FlickerLight> lights = new List<FlickerLight>();
        private readonly List<bool> engaged = new List<bool>();
        private readonly List<float> lastCrackle = new List<float>();
        private readonly List<float> level = new List<float>();
        private GameFlowController flow;
        private GameAudio audioPlayer;
        private float nextCheck;
        private float lastAnyCrackle = -100f;

        public void Initialize(GameFlowController flow, GameAudio audioPlayer, IEnumerable<FlickerLight> flickers)
        {
            this.flow = flow;
            this.audioPlayer = audioPlayer;
            foreach (FlickerLight f in flickers)
            {
                if (f == null) continue;
                lights.Add(f);
                engaged.Add(false);
                lastCrackle.Add(-100f);
                level.Add(0f);
            }
        }

        private void Update()
        {
            if (flow == null)
                return;

            float dt = Time.deltaTime;
            bool check = Time.time >= nextCheck;
            if (check)
                nextCheck = Time.time + 0.1f;

            GhostMotionController ghost = flow.Ghost;
            bool present = ghost != null && !ghost.Dormant && !ghost.Halted && ghost.Visual != null && ghost.Visual.Visibility > 0.5f;
            Vector3 at = present ? ghost.ChestPosition : Vector3.zero;

            for (int i = 0; i < lights.Count; i++)
            {
                FlickerLight f = lights[i];
                if (f == null) continue;

                float target = 0f;
                if (present)
                {
                    float d = Vector3.Distance(f.transform.position, at);
                    if (check)
                    {
                        bool was = engaged[i];
                        engaged[i] = was ? d < Exit : d < Enter;
                        if (!was && engaged[i])
                            Crackle(i, f);
                    }
                    if (engaged[i])
                        target = Mathf.InverseLerp(Exit, Full, d);
                }
                else if (check)
                {
                    engaged[i] = false;
                }

                // Recovers more slowly than it fails.
                level[i] = Mathf.MoveTowards(level[i], target, dt * (target > level[i] ? 3f : 0.8f));
                f.SetDisturbance(level[i]);
            }
        }

        private void Crackle(int i, FlickerLight f)
        {
            float now = Time.time;
            Light l = f.GetComponent<Light>();
            if (audioPlayer == null || l == null || !l.enabled || now - lastCrackle[i] < LightCooldown || now - lastAnyCrackle < GlobalCooldown)
                return;
            lastCrackle[i] = now;
            lastAnyCrackle = now;
            audioPlayer.PlayAt(AudioCue.Flicker, f.transform.position, 0.55f, Random.Range(0.85f, 1.15f), 0, 1f, 12f);
        }
    }
}
