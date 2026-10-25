using System.Threading.Tasks;
using PHYSXR.Game.Audio;
using UnityEngine;

namespace PHYSXR.App
{
    // The welcome screen's sound, owned by MainMenu on the persistent
    // [Systems] object. It is the only menu audio system: one music source,
    // one ambience source and two one-shot sources, created once, so returning
    // to the menu or restarting never stacks music.
    //
    //   Enter(first)   music and storm ambience fade in; the music restarts
    //                  from its top with a fade, never with a jump.
    //   TitleAccent    the reveal swell, once per process launch.
    //   Hover/Select   quiet UI ticks (hover rate-limited).
    //   BeginCue       the transition cue; music and ambience fade out over
    //                  ~2.5 s. Because the sources live on [Systems] (not the
    //                  menu camera), the tail carries across the scene load
    //                  under the house's own ambience.
    //   Leave          fade out (menu hidden for any reason).
    //
    // All volumes sit under the master volume (AudioListener.volume, from
    // GameSettings) and stay well below dialogue level. The music and cues are
    // rendered on a worker thread (MenuSynth) and turned into clips once; until
    // then only the ambience plays. Nothing is rendered in batch mode.
    public class MenuSoundscape : MonoBehaviour
    {
        public const float MusicLevel = 0.3f;
        public const float AmbienceLevel = 0.14f;
        public const float FadeInSeconds = 4f;
        public const float FadeOutSeconds = 2.5f;

        private static Task<MenuSynth.Bank> render;
        private static AudioClip music, accent, beginCue, hover, select;

        private AudioSource musicSource, ambienceSource, oneShots, cues;
        private float musicTarget, ambienceTarget;
        private float fadeRate = 1f / FadeInSeconds;
        private float lastHover = -1f;
        private bool wantMusic;
        private bool accentPlayed;

        public bool MusicReady => music != null;

        public void Initialize()
        {
            musicSource = MakeSource("MenuMusic", true);
            ambienceSource = MakeSource("MenuAmbience", true);
            oneShots = MakeSource("MenuUi", false);
            cues = MakeSource("MenuCues", false);

            ambienceSource.clip = GameAudio.Clip(AudioCue.AmbienceStorm, 0, out _);

            if (render == null && !Application.isBatchMode)
                render = Task.Run(() => MenuSynth.RenderAll());
        }

        private AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            s.volume = 0f;
            s.priority = loop ? 64 : 96;
            return s;
        }

        public void Enter(bool first)
        {
            wantMusic = true;
            fadeRate = 1f / FadeInSeconds;
            musicTarget = MusicLevel;
            ambienceTarget = AmbienceLevel;
            if (ambienceSource.clip != null && !ambienceSource.isPlaying)
                ambienceSource.Play();
            StartMusicIfReady();
        }

        public void Leave()
        {
            wantMusic = false;
            fadeRate = 1f / FadeOutSeconds;
            musicTarget = 0f;
            ambienceTarget = 0f;
        }

        public void TitleAccent()
        {
            if (accentPlayed || accent == null)
                return;
            accentPlayed = true;
            cues.PlayOneShot(accent, 0.42f);
        }

        public void Hover()
        {
            if (hover == null || Time.unscaledTime - lastHover < 0.06f)
                return;
            lastHover = Time.unscaledTime;
            oneShots.PlayOneShot(hover, 0.16f);
        }

        public void Select()
        {
            if (select != null)
                oneShots.PlayOneShot(select, 0.3f);
            else
                oneShots.PlayOneShot(GameAudio.Clip(AudioCue.UiSelect, 0, out float v), v);
        }

        public void Thunder()
        {
            AudioClip clip = GameAudio.Clip(AudioCue.Thunder, Random.Range(0, 3), out float v);
            if (clip != null)
                cues.PlayOneShot(clip, v * 0.4f);
        }

        public void BeginCue()
        {
            if (beginCue != null)
                cues.PlayOneShot(beginCue, 0.5f);
            Leave();
        }

        private void StartMusicIfReady()
        {
            if (!wantMusic || music == null)
                return;
            if (musicSource.clip != music)
                musicSource.clip = music;
            if (!musicSource.isPlaying)
            {
                musicSource.volume = 0f;
                musicSource.time = 0f;
                musicSource.Play();
            }
        }

        private void Update()
        {
            if (music == null && render != null && render.IsCompleted)
                CreateClips();

            float step = fadeRate * Time.unscaledDeltaTime;
            Fade(musicSource, musicTarget, step * MusicLevel);
            Fade(ambienceSource, ambienceTarget, step * AmbienceLevel);
        }

        private static void Fade(AudioSource s, float target, float step)
        {
            if (s == null)
                return;
            s.volume = Mathf.MoveTowards(s.volume, target, step);
            if (target <= 0f && s.volume <= 0.0001f && s.isPlaying)
                s.Stop();
        }

        private void CreateClips()
        {
            if (render.IsFaulted)
            {
                Debug.LogWarning("[MenuSoundscape] music render failed: " + render.Exception?.GetBaseException().Message);
                render = null;
                return;
            }
            MenuSynth.Bank b = render.Result;
            music = Clip("PHYSXR_MenuTheme", b.Music, 2);
            accent = Clip("PHYSXR_MenuAccent", b.Accent, 2);
            beginCue = Clip("PHYSXR_MenuBegin", b.Begin, 2);
            hover = Clip("PHYSXR_MenuHover", b.Hover, 1);
            select = Clip("PHYSXR_MenuSelect", b.Select, 1);
            StartMusicIfReady();
        }

        private static AudioClip Clip(string name, float[] data, int channels)
        {
            AudioClip c = AudioClip.Create(name, data.Length / channels, channels, MenuSynth.Rate, false);
            c.SetData(data, 0);
            c.hideFlags = HideFlags.DontSave;
            return c;
        }
    }
}
