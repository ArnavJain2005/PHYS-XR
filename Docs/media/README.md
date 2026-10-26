# README media

Images used by the [main README](../../README.md) and the docs. Every image here is a real capture or a diagram drawn from the code; none is a mock-up.

## Still needed

Each item has a visible **TODO(media)** note at the place in the README where it belongs, naming the exact file. These notes are for the staging branch and must be replaced (or removed with the item listed here as outstanding) before merge. Never substitute a mock-up or an unrelated clip.

| File to add | What it should show | Length / size | Where it goes |
|---|---|---|---|
| `Docs/media/physxr-physical-loop-demo.mp4` | Split screen. Left: the real room, someone leaning in to the station and knocking it. Right: the game at the same moment, the ghost turning and walking off to search. One continuous take, F1 overlay visible | 60–90 s, 1920×1080, 30 fps or more | README top. GitHub does not play a committed `.mp4` inline in a README; also upload it through an issue/PR comment or a Release and link a poster frame to that URL |
| `Docs/media/the-presence-gameplay.gif` | Normal play: the corridor, the first-sighting lunge, the lantern stagger | 8–12 s, about 800 px wide, under 10 MB | README, under the hero image |
| `Docs/media/physxr-sensor-station.jpg` | The actual assembled station, as it is, connected to the PC | about 1600×1067 JPEG, well lit | README, "The sensor station" |
| `Docs/media/quest-link-demo.mp4` | An authentic PC VR recording over Quest Link (Quest 2 or Quest 3), with the station driving a reaction | 20–40 s, the headset's capture resolution | README, "Status and roadmap" |
| `Docs/media/social-preview-1200x630.png` | The Hollow hero frame with the project name, for GitHub's social preview (Settings → General → Social preview) | 1200×630 PNG, under 1 MB | Repository settings, not the README |

## In use

| File | Shows | Source |
|---|---|---|
| `hero_hollow_profile_2026-10-22.jpg` and the five other `*_2026-10-22.jpg` frames | In-game frames | The game's scripted `-physxr-capture` tour of 22 Oct 2026 (desktop, not a headset) |
| `menu_sensor_panel_2026-10-24.jpg` | The menu's sensor panel with no station plugged in | The menu's capture mode, 24 Oct 2026 |
| `station_flow.svg` | Sensor station data flow: ToF and MPU6050 → ESP32 → USB serial → Unity validation → `WorldState` | Drawn from `Firmware/physxr_station/physxr_station.ino` (pins, I²C addresses, rates, baud) |
| `hardware_schematic_poster_2025-05-25.jpg` | The team's hardware schematic poster | Used by [`Firmware/README.md`](../../Firmware/README.md) |

Older captures from before the 22 Oct presentation pass (`hero_corridor_follow.jpg`, `first_sighting_e5.jpg`, `kitchen_fuse_objective.jpg`, `encounter_lantern_prompt.jpg`, `power_restored_surge.jpg`, `ending_window_silhouette.jpg`, `menu_sensor_condition.jpg`) are no longer linked from any document.
