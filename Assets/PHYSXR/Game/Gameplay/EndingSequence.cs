using System.Collections;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Player;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The ending (GDD 4.2, step 7): after the player opens the front door,
    // the screen fades, the player stands in the front yard in the rain, and
    // lightning shows a silhouette in an upper window while a recording from
    // Elias plays as subtitles over radio static. Then the end card.
    //
    // Comfort rules (spec section F): the camera is never moved by the
    // sequence while visible - the player is placed during the black fade and
    // held still; everything else is world animation, light and sound.
    //
    // The UI reads Fade, Subtitle and Phase every frame; the sequence never
    // reaches into the UI. Skip() jumps to the end card.
    public class EndingSequence : MonoBehaviour
    {
        public enum Stage
        {
            Idle,
            Playing,
            EndCard
        }

        private static readonly string[] Recording =
        {
            "[A recording, found later in the caretaker's coat]",
            "ELIAS: If someone finds this... I was wrong about it.",
            "ELIAS: It never hunted me. Every time I went for the fuse box, it stood in my way.",
            "ELIAS: It wasn't following me. It was waiting for me to understand.",
            "ELIAS: Whatever is in this house, it was keeping something in. Not keeping me out.",
            "[The recording ends]"
        };

        private GameFlowController flow;
        private PlayerController player;
        private DefensiveAbility lantern;
        private StormController storm;
        private Transform exterior;
        private GameAudio audioPlayer;
        private AudioSource staticLoop;
        private Coroutine running;
        private Vector3 endingPosition;

        public Stage Phase { get; private set; } = Stage.Idle;

        // 0 = clear, 1 = black.
        public float Fade { get; private set; }

        public string Subtitle { get; private set; } = string.Empty;

        public void Initialize(GameFlowController flow, PlayerController player, DefensiveAbility lantern,
            StormController storm, Transform exterior, GameAudio audioPlayer, Vector3 endingPosition)
        {
            this.endingPosition = endingPosition;
            this.flow = flow;
            this.player = player;
            this.lantern = lantern;
            this.storm = storm;
            this.exterior = exterior;
            this.audioPlayer = audioPlayer;
            flow.EndingStarted += Begin;
        }

        private void OnDestroy()
        {
            if (flow != null)
                flow.EndingStarted -= Begin;
        }

        private void Begin()
        {
            if (running != null || Phase != Stage.Idle)
                return;
            Phase = Stage.Playing;
            running = StartCoroutine(Play());
        }

        public void Skip()
        {
            if (Phase != Stage.Playing)
                return;
            if (running != null)
                StopCoroutine(running);
            running = null;
            Subtitle = string.Empty;
            if (staticLoop != null)
                staticLoop.Stop();
            Fade = 1f;
            Phase = Stage.EndCard;
            audioPlayer?.Play2D(AudioCue.WinSting);
        }

        private IEnumerator Play()
        {
            yield return FadeTo(1f, 1.4f);

            lantern?.SetLit(false);
            if (exterior != null)
                exterior.gameObject.SetActive(true);
            storm?.SetOutdoors(true);
            player.Respawn(endingPosition, 0f);
            player.Motor.Look(new Vector2(0f, 9f));
            Transform figure = exterior != null ? exterior.Find("Silhouette") : null;
            Renderer window = exterior != null ? exterior.Find("SilhouetteWindow")?.GetComponent<Renderer>() : null;

            yield return new WaitForSeconds(0.8f);
            yield return FadeTo(0f, 2.2f);
            yield return new WaitForSeconds(1.6f);

            storm?.FlashNow(true);
            yield return new WaitForSeconds(0.05f);
            if (figure != null) figure.gameObject.SetActive(true);
            if (window != null) window.sharedMaterial = HouseMaterials.Get(HouseMaterials.Surface.LitWindow);
            audioPlayer?.Play2D(AudioCue.GhostWhisper, 0.5f);
            yield return new WaitForSeconds(1.2f);

            staticLoop = GameAudio.CreateLoop(AudioCue.RadioStatic, transform, Vector3.zero, false, 0.8f);
            foreach (string line in Recording)
            {
                Subtitle = line;
                yield return new WaitForSeconds(line.StartsWith("[") ? 2.6f : 4.2f);
            }
            Subtitle = string.Empty;
            staticLoop.Stop();

            storm?.FlashNow(true);
            yield return new WaitForSeconds(0.1f);
            if (figure != null) figure.gameObject.SetActive(false);
            if (window != null) window.sharedMaterial = HouseMaterials.Get(HouseMaterials.Surface.WindowStorm);
            yield return new WaitForSeconds(2.2f);

            yield return FadeTo(1f, 2.4f);
            audioPlayer?.Play2D(AudioCue.WinSting);
            Phase = Stage.EndCard;
            running = null;
        }

        private IEnumerator FadeTo(float target, float seconds)
        {
            float start = Fade;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Fade = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            Fade = target;
        }
    }
}
