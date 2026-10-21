using System;
using UnityEngine;

namespace PHYSXR.Game.Audio
{
    // Optional mapping from an AudioCue to a recorded clip, for local builds
    // that have licensed sound effects imported (see PHYS-XR > Art > Import
    // Local Third-Party Audio). The game never requires it: when the asset is
    // absent, or a cue has no entry, the procedural clip is used.
    [CreateAssetMenu(menuName = "PHYS-XR/Audio Override Library")]
    public class AudioOverrideLibrary : ScriptableObject
    {
        public const string ResourceName = "PHYSXR_AudioOverrides";

        [Serializable]
        public struct Entry
        {
            public AudioCue cue;
            public AudioClip clip;
            [Range(0f, 2f)] public float volume;
            public string source;
        }

        public Entry[] entries = new Entry[0];

        public bool TryGet(AudioCue cue, out AudioClip clip, out float volume)
        {
            foreach (Entry e in entries)
            {
                if (e.cue == cue && e.clip != null)
                {
                    clip = e.clip;
                    volume = e.volume <= 0f ? 1f : e.volume;
                    return true;
                }
            }

            clip = null;
            volume = 1f;
            return false;
        }
    }
}
