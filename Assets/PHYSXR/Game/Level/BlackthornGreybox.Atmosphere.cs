using System.Collections.Generic;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Gameplay;
using UnityEngine;
using S = PHYSXR.Game.Art.HouseMaterials.Surface;

namespace PHYSXR.Game.Level
{
    public partial class BlackthornGreybox
    {
        private readonly List<Light> stormLights = new List<Light>();

        public HorrorPostFx PostFx { get; private set; }

        // ------------------------------------------------------------- lights

        private void BuildLights(Transform parent)
        {
            Transform lights = Group(parent, "Lights");

            Chandelier(lights, "Hall_Chandelier", new Vector3(0f, 2.55f, -4f), 8f, 12f);
            Sconce(lights, "Corridor_SconceW", new Vector3(-5.12f, 2.05f, 3.6f), Vector3.right, 0.6f);
            Sconce(lights, "Corridor_SconceE", new Vector3(-2.88f, 2.05f, 6.6f), Vector3.left, 0.15f);
            Pendant(lights, "Kitchen_Pendant", new Vector3(-6.4f, 2.35f, 14.4f), 6f, 8f, true, 0.12f);
            Pendant(lights, "Kitchen_SinkLamp", new Vector3(-4f, 2.65f, 15.1f), 3f, 6f, false, 0f);
            Pendant(lights, "Utility_Bulb", new Vector3(4f, 2.55f, 12f), 6f, 8f, true, 0.25f);
            Tube(lights, "Utility_Tube", new Vector3(2f, 2.93f, 13.5f));
            Chandelier(lights, "Dining_Chandelier", new Vector3(6f, 2.45f, 4f), 7f, 10f);

            Emergency(lights, "Emergency_Entry", new Vector3(0f, 2.75f, -6.6f), 1.8f, 8f);
            Emergency(lights, "Emergency_Corridor", new Vector3(-4f, 2.85f, 4f), 1.3f, 7f);
            Emergency(lights, "Emergency_Kitchen", new Vector3(-1.2f, 2.8f, 9f), 1.0f, 6f);
            Emergency(lights, "Emergency_Utility", new Vector3(6.2f, 2.75f, 12.4f), 1.4f, 7f);
            Emergency(lights, "Emergency_Dining", new Vector3(2f, 2.8f, 7.2f), 1.2f, 7f);

            Candles(lights);
        }

        private Light Fixture(Transform parent, string name, Vector3 position, float intensity, float range, bool shadows, float roughness,
            Renderer bulb, Color color)
        {
            Light light = PointLight(parent, name, position, color, intensity, range, shadows);
            light.shadowStrength = 0.8f;
            FlickerLight flicker = light.gameObject.AddComponent<FlickerLight>();
            flicker.Initialize(light, roughness, bulb, HouseMaterials.EmissionOf(S.BulbOn));
            mainLights.Add(light);
            if (bulb != null)
                bulbs.Add(bulb);
            return light;
        }

        private void Chandelier(Transform parent, string name, Vector3 at, float intensity, float range)
        {
            Transform g = Group(parent, name);
            Block(g, "Chain", at + Vector3.up * 0.25f, new Vector3(0.03f, 0.5f, 0.03f), S.Brass, false);
            Cylinder(g, "Ring", at, 0.42f, 0.04f, S.Brass, false);
            Renderer first = null;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector3 p = at + new Vector3(Mathf.Cos(a) * 0.4f, 0.08f, Mathf.Sin(a) * 0.4f);
                Cylinder(g, "Holder", p - Vector3.up * 0.04f, 0.035f, 0.08f, S.Brass, false);
                GameObject b = Sphere(g, "Bulb", p + Vector3.up * 0.05f, new Vector3(0.06f, 0.09f, 0.06f), S.BulbOff);
                if (i == 0) first = b.GetComponent<Renderer>();
                else bulbs.Add(b.GetComponent<Renderer>());
            }
            Fixture(g, name + "_Light", at + Vector3.down * 0.1f, intensity, range, true, 0.1f, first, new Color(1f, 0.82f, 0.58f));
        }

        private void Pendant(Transform parent, string name, Vector3 at, float intensity, float range, bool shadows, float roughness)
        {
            Transform g = Group(parent, name);
            float cord = WallHeight - at.y - 0.12f;
            Block(g, "Cord", at + Vector3.up * (0.12f + cord / 2f), new Vector3(0.015f, cord, 0.015f), S.Black, false);
            Cylinder(g, "Shade", at + Vector3.up * 0.07f, 0.24f, 0.16f, S.Metal, false, null, 0.05f);
            GameObject bulb = Sphere(g, "Bulb", at, new Vector3(0.09f, 0.11f, 0.09f), S.BulbOff);
            Fixture(g, name + "_Light", at + Vector3.down * 0.08f, intensity, range, shadows, roughness, bulb.GetComponent<Renderer>(),
                new Color(1f, 0.8f, 0.55f));
        }

        private void Sconce(Transform parent, string name, Vector3 at, Vector3 inward, float roughness)
        {
            Transform g = Group(parent, name);
            Quaternion rot = Quaternion.LookRotation(inward);
            Block(g, "Plate", at - inward * 0.06f, new Vector3(0.12f, 0.2f, 0.02f), S.Brass, false, rot);
            Block(g, "Arm", at - inward * 0.03f, new Vector3(0.02f, 0.02f, 0.08f), S.Brass, false, rot);
            Cylinder(g, "Shade", at + Vector3.up * 0.05f, 0.08f, 0.12f, S.Paper, false, null, 0.05f);
            GameObject bulb = Sphere(g, "Bulb", at, new Vector3(0.05f, 0.07f, 0.05f), S.BulbOff);
            Fixture(g, name + "_Light", at + inward * 0.1f, 3.5f, 6f, false, roughness, bulb.GetComponent<Renderer>(), new Color(1f, 0.78f, 0.52f));
        }

        private void Tube(Transform parent, string name, Vector3 at)
        {
            Transform g = Group(parent, name);
            Block(g, "Housing", at + Vector3.up * 0.03f, new Vector3(1.3f, 0.05f, 0.16f), S.Metal, false);
            GameObject tube = Block(g, "Tube", at - Vector3.up * 0.015f, new Vector3(1.2f, 0.04f, 0.05f), S.BulbOff, false);
            Light light = Fixture(g, name + "_Light", at + Vector3.down * 0.2f, 3.5f, 6.5f, false, 0.85f, tube.GetComponent<Renderer>(),
                new Color(0.82f, 0.92f, 1f));
            Power.RegisterPoweredLoop(GameAudio.CreateLoop(AudioCue.LightBuzz, light.transform, Vector3.zero, true, 1f, 1f, 7f));
        }

        private void Emergency(Transform parent, string name, Vector3 at, float intensity, float range)
        {
            Transform g = Group(parent, name);
            Block(g, "Housing", at + Vector3.up * 0.06f, new Vector3(0.22f, 0.08f, 0.12f), S.Metal, false);
            GameObject lamp = Block(g, "Lamp", at, new Vector3(0.16f, 0.05f, 0.08f), S.EmergencyOn, false);
            emergencyBulbs.Add(lamp.GetComponent<Renderer>());
            Light light = PointLight(g, name + "_Light", at + Vector3.down * 0.1f, new Color(0.85f, 0.12f, 0.08f), intensity, range, false);
            emergencyLights.Add(light);
        }

        private void Candles(Transform parent)
        {
            Transform g = Group(parent, "Dining_Candles");
            for (int i = 0; i < 3; i++)
            {
                Vector3 b = new Vector3(6f, 0.78f, 3.2f + i * 0.8f);
                Cylinder(g, "Holder", b + Vector3.up * 0.08f, 0.04f, 0.16f, S.Brass, false, null, 0.02f);
                float h = 0.12f + (i % 2) * 0.06f;
                Cylinder(g, "Candle", b + Vector3.up * (0.16f + h / 2f), 0.018f, h, S.Porcelain, false);
                Sphere(g, "Flame", b + Vector3.up * (0.18f + h), new Vector3(0.02f, 0.045f, 0.02f), S.CandleFlame);
            }
            Light light = PointLight(g, "CandleLight", new Vector3(6f, 1.15f, 4f), new Color(1f, 0.6f, 0.3f), 2.2f, 5f, false);
            light.gameObject.AddComponent<FlickerLight>().Initialize(light, 1f);
            GameAudio.CreateLoop(AudioCue.CandleCrackle, g, new Vector3(6f, 1f, 4f), true, 1f, 0.5f, 4f);
        }

        // ------------------------------------------------------ interactables

        private void BuildInteractables(Transform parent)
        {
            Transform g = Group(parent, "Clues");

            Note(g, "Note_Log", new Vector3(-3.0f, 0.86f, -7.66f), 15f,
                "Caretaker's log - E. Voss",
                "The power went again at dusk. The fuse box in the utility room is empty; the old fuses blew in the storm. "
                + "I left the spare on the kitchen table.\n\nSomething stood at the end of the corridor tonight. It did not come closer. "
                + "It watched me walk to the kitchen.");
            Note(g, "Note_Kitchen", new Vector3(-7.6f, 0.92f, 10.25f), -70f,
                "A note under the bread bin",
                "It never touches me. When I go near the fuse box it stands in the doorway, as if it wants me to stop.\n\n"
                + "The lantern makes it flinch. Bright light, close up. It does not hurt it. It just... remembers itself.");
            Note(g, "Note_Workbench", new Vector3(2.1f, 0.93f, 15.45f), 5f,
                "A torn page",
                "If the lights come back, it changes. It stops waiting.\n\n"
                + "Do not stand still near the front door. Raise the lantern when it gets close, then keep walking.");

            GameObject radio = Block(g, "Radio", new Vector3(0.38f, 1.12f, 3.5f), new Vector3(0.3f, 0.24f, 0.45f), S.DarkWood);
            Block(g, "RadioDial", new Vector3(0.54f, 1.12f, 3.58f), new Vector3(0.02f, 0.12f, 0.2f), S.LitWindow, false);
            ReadableNote radioNote = radio.AddComponent<ReadableNote>();
            radioNote.Initialize(FlowController, "The radio, between stations",
                "...storm warning remains in force for the Blackthorn coast... residents are advised... [static] "
                + "...police are still asking for information about the caretaker reported missing three weeks ago...",
                "Turn the radio dial");
            notes.Add(radioNote);

            LockedDoorOnWall(g, "StudyDoor", new Vector3(-5.2f, 0f, 4.6f), Vector3.right, "Locked. A brass plate reads: Study.");
            LockedDoorOnWall(g, "CellarDoor", new Vector3(-2.8f, 0f, 5.0f), Vector3.left, "Locked. Cold air seeps under it.");
        }

        private void Note(Transform parent, string name, Vector3 at, float yaw, string title, string body)
        {
            GameObject paper = Block(parent, name, at, new Vector3(0.21f, 0.008f, 0.29f), S.Paper, true, Quaternion.Euler(0f, yaw, 0f), 0.3f, false);
            // A slightly larger trigger-free collider makes the paper easy to aim at.
            paper.GetComponent<BoxCollider>().size = new Vector3(0.32f, 0.08f, 0.4f);
            ReadableNote note = paper.AddComponent<ReadableNote>();
            note.Initialize(FlowController, title, body);
            notes.Add(note);
        }

        private void LockedDoorOnWall(Transform parent, string name, Vector3 wallFaceFloor, Vector3 inward, string message)
        {
            Quaternion rot = Quaternion.LookRotation(inward);
            Vector3 right = rot * Vector3.right;
            Vector3 c = wallFaceFloor + Vector3.up * 1.1f;
            GameObject slab = Block(parent, name, c + inward * 0.03f, new Vector3(0.95f, 2.2f, 0.05f), S.DarkWood, true, rot);
            Block(parent, name + "_FrameL", c + inward * 0.04f - right * 0.52f, new Vector3(0.08f, 2.2f, 0.07f), S.Trim, false, rot);
            Block(parent, name + "_FrameR", c + inward * 0.04f + right * 0.52f, new Vector3(0.08f, 2.2f, 0.07f), S.Trim, false, rot);
            Block(parent, name + "_FrameT", c + inward * 0.04f + Vector3.up * 1.14f, new Vector3(1.12f, 0.1f, 0.07f), S.Trim, false, rot);
            Block(parent, name + "_Knob", c + inward * 0.08f + right * 0.36f - Vector3.up * 0.1f, new Vector3(0.05f, 0.05f, 0.05f), S.Brass, false, rot);
            slab.AddComponent<LockedDoor>().Initialize(FlowController, Audio, message);
        }

        // -------------------------------------------------------------- triggers

        private void BuildTriggers()
        {
            Transform g = new GameObject("Triggers").transform;
            g.SetParent(transform, false);

            Trigger(g, "FirstSighting_Corridor", new Vector3(-4f, 1f, 1.6f), new Vector3(2.4f, 2f, 0.6f))
                .Entered += _ => FlowController.TriggerFirstSighting(Anchors.FirstSighting);
            Trigger(g, "FirstSighting_Dining", new Vector3(4f, 1f, 1.0f), new Vector3(1.4f, 2f, 0.6f))
                .Entered += _ => FlowController.TriggerFirstSighting(FirstSightingAlt);
            Trigger(g, "Encounter_Dining", new Vector3(4f, 1f, 7.3f), new Vector3(1.4f, 2f, 0.6f))
                .Entered += _ => FlowController.TriggerEncounter("dining");
            Trigger(g, "Encounter_Kitchen", new Vector3(-0.7f, 1f, 12f), new Vector3(0.6f, 2f, 1.4f))
                .Entered += _ => FlowController.TriggerEncounter("kitchen");
        }

        private GameTriggerVolume Trigger(Transform parent, string id, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            GameTriggerVolume trigger = go.AddComponent<GameTriggerVolume>();
            trigger.Initialize(id, Player.transform, size);
            return trigger;
        }

        // ------------------------------------------------------------ atmosphere

        private void BuildAtmosphere(Transform systems)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.016f, 0.018f, 0.024f);
            RenderSettings.fogDensity = 0.05f;

            GameAudio.CreateLoop(AudioCue.AmbienceHouse, systems, Vector3.zero, false, 1f);
            AudioSource storm = GameAudio.CreateLoop(AudioCue.AmbienceStorm, systems, Vector3.zero, false, 1f);
            AudioLowPassFilter muffle = storm.gameObject.AddComponent<AudioLowPassFilter>();
            muffle.cutoffFrequency = 750f;
            GameAudio.CreateLoop(AudioCue.ClockTick, interior, new Vector3(-7.5f, 1.6f, -6f), true, 1f, 0.8f, 9f);
            Power.RegisterPoweredLoop(GameAudio.CreateLoop(AudioCue.PowerHum, interior, FuseBoxPosition, true, 1f, 1f, 8f));

            Reverb("Reverb_Entry", new Vector3(0f, 1.5f, -4f), AudioReverbPreset.Hallway, 5f, 9f);
            Reverb("Reverb_Corridor", new Vector3(-4f, 1.5f, 4f), AudioReverbPreset.StoneCorridor, 2f, 4.5f);
            Reverb("Reverb_Kitchen", new Vector3(-4f, 1.5f, 12f), AudioReverbPreset.Room, 3f, 5.5f);
            Reverb("Reverb_Utility", new Vector3(4f, 1.5f, 12f), AudioReverbPreset.Bathroom, 3f, 5.5f);
            Reverb("Reverb_Dining", new Vector3(4f, 1.5f, 4f), AudioReverbPreset.Livingroom, 3f, 5.5f);

            Storm = systems.gameObject.AddComponent<StormController>();
            Storm.Initialize(Audio, storm, muffle, stormLights, moonLight, Player.transform, interior);

            PostFx = systems.gameObject.AddComponent<HorrorPostFx>();
            PostFx.Initialize(FlowController);
        }

        private void Reverb(string name, Vector3 at, AudioReverbPreset preset, float minDistance, float maxDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(interior, false);
            go.transform.position = at;
            AudioReverbZone zone = go.AddComponent<AudioReverbZone>();
            zone.reverbPreset = preset;
            zone.minDistance = minDistance;
            zone.maxDistance = maxDistance;
        }

        // ---------------------------------------------------------- exterior set

        private Light moonLight;

        // The front yard and facade for the ending (spec section C: "a
        // separate small set in the same scene, shown after a fade"). It stays
        // inactive until the ending sequence enables it.
        private void BuildExterior(Transform root)
        {
            Block(root, "Ground", new Vector3(0f, -0.05f, -24.1f), new Vector3(50f, 0.1f, 32f), S.Ground, true, null, 4f, false);
            Block(root, "Path", new Vector3(0f, 0.005f, -14.5f), new Vector3(1.6f, 0.01f, 12.8f), S.Stone, false, null, 1.5f, false);
            Block(root, "Doormat", new Vector3(0f, 0.01f, -8.7f), new Vector3(1.0f, 0.02f, 0.6f), S.Rug, false);

            const float fz = -8.27f;
            Block(root, "Facade_L", new Vector3(-4.6f, 1.5f, fz), new Vector3(7.7f, 3f, 0.12f), S.Siding, false, null, 2.5f);
            Block(root, "Facade_R", new Vector3(4.6f, 1.5f, fz), new Vector3(7.7f, 3f, 0.12f), S.Siding, false, null, 2.5f);
            Block(root, "Facade_OverDoor", new Vector3(0f, 2.73f, fz), new Vector3(1.5f, 0.54f, 0.12f), S.Siding, false, null, 2.5f);
            Block(root, "Facade_Upper", new Vector3(0f, 4.7f, fz), new Vector3(16.9f, 3.4f, 0.12f), S.Siding, false, null, 2.5f);
            Block(root, "Band", new Vector3(0f, 3.02f, fz - 0.04f), new Vector3(16.9f, 0.12f, 0.08f), S.Trim, false);
            Block(root, "CornerL", new Vector3(-8.4f, 3.2f, fz - 0.03f), new Vector3(0.18f, 6.4f, 0.14f), S.Trim, false);
            Block(root, "CornerR", new Vector3(8.4f, 3.2f, fz - 0.03f), new Vector3(0.18f, 6.4f, 0.14f), S.Trim, false);

            ExteriorWindow(root, "Lower_L", new Vector3(-5.5f, 1.6f, fz), 1.0f, 1.4f, S.LitWindow);
            ExteriorWindow(root, "Lower_R", new Vector3(5.5f, 1.6f, fz), 1.0f, 1.4f, S.LitWindow);
            ExteriorWindow(root, "Upper_L", new Vector3(-4.5f, 4.7f, fz), 0.9f, 1.3f, S.WindowStorm);
            GameObject upper = ExteriorWindow(root, "Upper_R", new Vector3(3.0f, 4.7f, fz), 0.9f, 1.3f, S.WindowStorm);
            upper.name = "SilhouetteWindow";

            // The figure in the upper window, revealed by the ending sequence.
            Transform figure = new GameObject("Silhouette").transform;
            figure.SetParent(root, false);
            Block(figure, "Body", new Vector3(3.0f, 4.45f, fz - 0.1f), new Vector3(0.36f, 0.95f, 0.02f), S.Black, false);
            Block(figure, "Shoulders", new Vector3(3.0f, 4.86f, fz - 0.1f), new Vector3(0.5f, 0.14f, 0.02f), S.Black, false);
            Sphere(figure, "Head", new Vector3(3.02f, 5.08f, fz - 0.1f), new Vector3(0.2f, 0.3f, 0.02f), S.Black);
            Block(figure, "ArmL", new Vector3(2.76f, 4.42f, fz - 0.1f), new Vector3(0.06f, 0.75f, 0.02f), S.Black, false, Quaternion.Euler(0f, 0f, -4f));
            Block(figure, "ArmR", new Vector3(3.24f, 4.42f, fz - 0.1f), new Vector3(0.06f, 0.75f, 0.02f), S.Black, false, Quaternion.Euler(0f, 0f, 4f));
            figure.gameObject.SetActive(false);

            float rise = 2.2f, run = 5.6f;
            float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            Block(root, "Roof", new Vector3(0f, 6.4f + rise / 2f, fz + run / 2f - 0.3f), new Vector3(17.6f, 0.16f, Mathf.Sqrt(rise * rise + run * run) + 0.6f),
                S.Roof, false, Quaternion.Euler(-slope, 0f, 0f), 2f);
            Block(root, "Fascia", new Vector3(0f, 6.4f, fz - 0.35f), new Vector3(17.6f, 0.22f, 0.06f), S.Trim, false);
            Block(root, "Chimney", new Vector3(5f, 8.2f, -4.5f), new Vector3(0.8f, 2.6f, 0.8f), S.Stone, false);

            Block(root, "PorchRoof", new Vector3(0f, 2.86f, -9.05f), new Vector3(2.8f, 0.1f, 1.6f), S.Roof, false, Quaternion.Euler(8f, 0f, 0f));
            Block(root, "PorchPostL", new Vector3(-1.25f, 1.4f, -9.7f), new Vector3(0.12f, 2.8f, 0.12f), S.Trim, false);
            Block(root, "PorchPostR", new Vector3(1.25f, 1.4f, -9.7f), new Vector3(0.12f, 2.8f, 0.12f), S.Trim, false);
            Sphere(root, "PorchBulb", new Vector3(0.95f, 2.25f, fz - 0.15f), new Vector3(0.1f, 0.14f, 0.1f), S.BulbOn);
            PointLight(root, "PorchLight", new Vector3(0.95f, 2.2f, fz - 0.4f), new Color(1f, 0.75f, 0.45f), 3f, 6f, false);

            for (float x = -14f; x <= 14.01f; x += 1.6f)
            {
                if (Mathf.Abs(x) < 1.3f) continue;
                Block(root, "FencePost", new Vector3(x, 0.6f, -21f), new Vector3(0.1f, 1.2f, 0.1f), S.Bark, false);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Block(root, "FenceRailLow", new Vector3(side * 7.6f, 0.45f, -21f), new Vector3(12.8f, 0.06f, 0.04f), S.Bark, false);
                Block(root, "FenceRailHigh", new Vector3(side * 7.6f, 0.95f, -21f), new Vector3(12.8f, 0.06f, 0.04f), S.Bark, false);
            }
            Block(root, "Gate", new Vector3(0.55f, 0.6f, -21.4f), new Vector3(1.1f, 0.9f, 0.05f), S.Bark, false, Quaternion.Euler(0f, -55f, 0f));

            Tree(root, new Vector3(-9f, 0f, -15f), 5.5f, 1);
            Tree(root, new Vector3(10f, 0f, -17f), 6.5f, 2);
            Tree(root, new Vector3(-14f, 0f, -26f), 7f, 3);

#if UNITY_EDITOR
            if (drawingGizmos)
                return;
#endif
            var moon = new GameObject("MoonLight");
            moon.transform.SetParent(root, false);
            moon.transform.rotation = Quaternion.Euler(28f, 15f, 0f);
            moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.42f, 0.5f, 0.7f);
            moonLight.intensity = 0.35f;
            moonLight.shadows = LightShadows.Soft;

            var yardReverb = new GameObject("Reverb_Yard");
            yardReverb.transform.SetParent(root, false);
            yardReverb.transform.position = EndingPosition;
            AudioReverbZone zone = yardReverb.AddComponent<AudioReverbZone>();
            zone.reverbPreset = AudioReverbPreset.Forest;
            zone.minDistance = 8f;
            zone.maxDistance = 20f;

            root.gameObject.SetActive(false);
        }

        private GameObject ExteriorWindow(Transform root, string name, Vector3 centre, float width, float height, S pane)
        {
            GameObject glass = Block(root, name + "_Pane", centre + Vector3.back * 0.07f, new Vector3(width, height, 0.02f), pane, false);
            Block(root, name + "_Frame", centre + Vector3.back * 0.08f + Vector3.up * (height / 2f + 0.05f), new Vector3(width + 0.2f, 0.1f, 0.06f), S.Trim, false);
            Block(root, name + "_Sill", centre + Vector3.back * 0.1f - Vector3.up * (height / 2f + 0.05f), new Vector3(width + 0.3f, 0.08f, 0.12f), S.Trim, false);
            Block(root, name + "_Mullion", centre + Vector3.back * 0.09f, new Vector3(0.04f, height, 0.03f), S.Trim, false);
            Block(root, name + "_Shutter", centre + Vector3.back * 0.09f + Vector3.left * (width / 2f + 0.25f), new Vector3(0.4f, height + 0.1f, 0.04f), S.DarkWood, false);
            return glass;
        }

        private void Tree(Transform root, Vector3 at, float height, int seed)
        {
            Cylinder(root, "Trunk", at + Vector3.up * (height / 2f), 0.22f, height, S.Bark, false, null, 0.1f);
            for (int i = 0; i < 5; i++)
            {
                float y = height * (0.45f + i * 0.1f);
                float yaw = seed * 53f + i * 73f;
                float length = 1.6f - i * 0.2f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 55f - i * 6f);
                Cylinder(root, "Branch", at + Vector3.up * y + rot * (Vector3.up * length / 2f), 0.06f, length, S.Bark, false, rot, 0.02f);
            }
        }
    }
}
