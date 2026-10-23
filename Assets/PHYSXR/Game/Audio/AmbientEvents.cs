using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using UnityEngine;

namespace PHYSXR.Game.Audio
{
    // The house's own small noises, placed in the world so they have a
    // direction: knocks inside the walls a few metres away, an old pipe
    // groaning in the kitchen or utility room, and a leak that drips by the
    // kitchen sink. The weather and creaks stay with StormController.
    //
    // Everything is spaced out: one event every 9-22 s at most, never while
    // the score is near its climax (an encounter has the stage) and never
    // while the run is not playing. Each event uses the pooled 3D sources in
    // GameAudio, so nothing accumulates. Presentation only.
    public class AmbientEvents : MonoBehaviour
    {
        // Pipe runs: kitchen wall behind the sink, utility room boiler corner.
        private static readonly Vector3[] Pipes = { new Vector3(-6.5f, 2.6f, 15.7f), new Vector3(6.9f, 2.5f, 14.8f), new Vector3(2.4f, 2.7f, 15.6f) };
        private static readonly Vector3 Leak = new Vector3(-4f, 0.95f, 15.2f);

        private GameFlowController flow;
        private GameAudio audioPlayer;
        private TensionScore score;
        private Transform listener;
        private float nextEvent;
        private float nextDrip;

        public void Initialize(GameFlowController flow, GameAudio audioPlayer, TensionScore score, Transform listener)
        {
            this.flow = flow;
            this.audioPlayer = audioPlayer;
            this.score = score;
            this.listener = listener;
            nextEvent = Time.time + Random.Range(12f, 20f);
            nextDrip = Time.time + Random.Range(2f, 5f);
        }

        private void Update()
        {
            if (flow == null || audioPlayer == null || listener == null || flow.Flow.RunState != RunState.Playing)
                return;

            float now = Time.time;
            if (now >= nextDrip)
            {
                // Quiet and short-ranged: you only hear it in the kitchen.
                nextDrip = now + Random.Range(2.2f, 6.5f);
                audioPlayer.PlayAt(AudioCue.WaterDrip, Leak, 0.7f, Random.Range(0.92f, 1.08f), Random.Range(0, 3), 0.6f, 7f);
            }

            if (now < nextEvent)
                return;
            nextEvent = now + Random.Range(9f, 22f);
            if (score != null && score.Intensity > 0.7f)
                return;

            if (Random.value < 0.6f)
                Knock();
            else
                Groan();
        }

        // A knock behind a wall, 4-9 m from the player, inside the house.
        private void Knock()
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector3 at = listener.position + new Vector3(dir.x, 0f, dir.y) * Random.Range(4f, 9f);
            at.x = Mathf.Clamp(at.x, -7.8f, 7.8f);
            at.z = Mathf.Clamp(at.z, -7.8f, 15.8f);
            at.y = Random.Range(0.4f, 2.2f);
            audioPlayer.PlayAt(AudioCue.DistantKnock, at, 0.75f, Random.Range(0.9f, 1.05f), Random.Range(0, 3), 2f, 16f);
        }

        private void Groan()
        {
            Vector3 at = Pipes[Random.Range(0, Pipes.Length)];
            audioPlayer.PlayAt(AudioCue.PipeGroan, at, 0.8f, Random.Range(0.9f, 1.1f), Random.Range(0, 2), 2f, 20f);
        }
    }
}
