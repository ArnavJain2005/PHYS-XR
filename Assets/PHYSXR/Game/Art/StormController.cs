using System.Collections.Generic;
using PHYSXR.Game.Audio;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // The storm outside and the house's own noises: lightning (window panes
    // and flash lights, and the moon light during the ending), thunder after a
    // distance-dependent delay, rain muffled by the walls, and occasional
    // creaks somewhere near the player. Presentation only; it never touches
    // game rules. Runs on scaled time, so it waits while the game is paused.
    public class StormController : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color PaneDark = new Color(0.02f, 0.03f, 0.05f);
        private static readonly Color PaneFlash = new Color(1.6f, 1.8f, 2.3f);

        private GameAudio audioPlayer;
        private AudioSource rain;
        private AudioLowPassFilter muffle;
        private List<Light> windowLights;
        private Light moon;
        private Transform listener;
        private Transform interior;
        private Material pane;
        private float rainBaseVolume;
        private float moonBase;
        private float nextFlash;
        private float nextCreak;
        private float flashStart = -10f;
        private bool flashNear;
        private float thunderAt = -1f;
        private bool outdoors;

        public void Initialize(GameAudio audioPlayer, AudioSource rain, AudioLowPassFilter muffle, List<Light> windowLights,
            Light moon, Transform listener, Transform interior)
        {
            this.audioPlayer = audioPlayer;
            this.rain = rain;
            this.muffle = muffle;
            this.windowLights = windowLights;
            this.moon = moon;
            this.listener = listener;
            this.interior = interior;
            pane = HouseMaterials.Get(HouseMaterials.Surface.WindowStorm);
            rainBaseVolume = rain != null ? rain.volume : 0.4f;
            moonBase = moon != null ? moon.intensity : 0.35f;
            nextFlash = Time.time + Random.Range(8f, 16f);
            nextCreak = Time.time + Random.Range(6f, 12f);
            ApplyFlash(0f);
        }

        // The ending moves the listener outside: no muffling, louder rain.
        public void SetOutdoors(bool value)
        {
            outdoors = value;
            if (muffle != null)
                muffle.enabled = !value;
            if (rain != null)
                rain.volume = rainBaseVolume * (value ? 1.6f : 1f);
        }

        // Scripted strike (the ending's reveal).
        public void FlashNow(bool near)
        {
            flashStart = Time.time;
            flashNear = near;
            thunderAt = Time.time + (near ? 0.25f : 1.2f);
            nextFlash = Time.time + Random.Range(14f, 30f);
        }

        private void Update()
        {
            float t = Time.time;
            if (t >= nextFlash)
                FlashNow(Random.value > 0.6f);

            float since = t - flashStart;
            float k = 0f;
            if (since >= 0f && since < 0.9f)
            {
                // Two strokes: a sharp one, a gap, a softer one fading out.
                if (since < 0.07f) k = 1f;
                else if (since < 0.16f) k = 0.05f;
                else if (since < 0.3f) k = 0.75f;
                else k = Mathf.Lerp(0.6f, 0f, (since - 0.3f) / 0.6f);
            }
            ApplyFlash(k * (flashNear ? 1f : 0.6f));

            if (thunderAt > 0f && t >= thunderAt)
            {
                thunderAt = -1f;
                audioPlayer?.Play2D(AudioCue.Thunder, flashNear ? 1f : 0.65f, Random.Range(0, 3));
            }

            if (!outdoors && t >= nextCreak)
            {
                nextCreak = t + Random.Range(9f, 22f);
                Creak();
            }
        }

        private void ApplyFlash(float k)
        {
            if (pane != null)
                pane.SetColor(EmissionColorId, Color.Lerp(PaneDark, PaneFlash, k));

            if (windowLights != null)
            {
                foreach (Light l in windowLights)
                {
                    if (l == null) continue;
                    l.enabled = k > 0.01f && !outdoors;
                    l.intensity = 8f * k;
                }
            }

            if (moon != null)
                moon.intensity = moonBase + 3.5f * k;
        }

        // A creak somewhere 3-8 m from the player, inside the house.
        private void Creak()
        {
            if (audioPlayer == null || listener == null)
                return;

            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(3f, 8f);
            Vector3 at = listener.position + new Vector3(offset.x, 2.6f, offset.y);
            at.x = Mathf.Clamp(at.x, -7.5f, 7.5f);
            at.z = Mathf.Clamp(at.z, -7.5f, 15.5f);
            audioPlayer.PlayAt(AudioCue.HouseCreak, at, 0.8f, Random.Range(0.85f, 1.1f), Random.Range(0, 4), 2f, 18f);
        }

        private void OnDestroy()
        {
            // The pane material is shared and outlives the scene.
            if (pane != null)
                pane.SetColor(EmissionColorId, PaneDark);
        }
    }
}
