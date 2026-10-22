using System;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Which presentation the house is built with. The art direction is the
    // same; the lightweight profile only drops what is expensive or
    // uncomfortable on a standalone headset (Quest-class, Android):
    //  - lens distortion, chromatic aberration and film grain (comfort and
    //    a full-screen pass each);
    //  - shadows from the window lights (extra shadow-atlas slices);
    //  - dust volumes (transparent overdraw).
    // It is chosen automatically on mobile platforms, and can be forced on a
    // desktop build with "-physxr-lightweight" to check it without a headset.
    // Nothing in this profile has been measured on a headset yet.
    public static class PresentationProfile
    {
        public const string Argument = "-physxr-lightweight";

        public static bool Lightweight { get; } =
            Application.isMobilePlatform || Array.IndexOf(Environment.GetCommandLineArgs(), Argument) >= 0;
    }
}
