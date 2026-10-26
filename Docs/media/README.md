# README media

Images used by the [main README](../../README.md) and the docs. Every image here is a real capture or a diagram drawn from the code; none is a mock-up.

## Still needed

Each item has a `TODO(media)` comment at the place in the README where it belongs. Use the file name given, so the README only needs its comment swapped for the link.

| File to add | What it should show | Length / size | Where it goes |
|---|---|---|---|
| `split_screen_knock_demo.mp4`, plus a poster frame `split_screen_knock_demo.jpg` | Left half: the real room, someone leaning in to the station and knocking it. Right half: the headset view at the same moment, the ghost turning and walking off to search. One continuous take, F1 overlay visible on the game side | 60–90 s, 1920×1080, 30 fps or more; poster 1280×720 JPEG | README top. GitHub does not play an `.mp4` committed to the repo inside a README: upload the video by dragging it into an issue or PR comment (or attach it to a Release) and link the poster to that URL |
| `gameplay_loop.gif` | Normal play without the station: the corridor, the first sighting lunge, the lantern stagger | 8–12 s, 800 px wide, 15 fps, under 10 MB | README top, under the video |
| `station_photo.jpg` | The physical station as accepted: ESP32, VL53L0X and MPU6050 on their mount, with the USB cable to the PC in shot | 1600×1067 JPEG, well lit, neutral background | README, "The sensor station" |
| `quest_headset_clip.mp4`, plus a poster frame `quest_headset_clip.jpg` | The game running on Meta Quest 2 or Quest 3 as PC VR over Quest Link, recorded from the headset, with the station driving a reaction | 20–40 s, the headset's native capture resolution | README, "Status and roadmap", next to the Quest line |
| `social_preview.png` | The Hollow hero frame with the project name, for GitHub's social preview (Settings → General → Social preview) | 1280×640 PNG, under 1 MB | Repository settings, not the README |

## In use

| File | Shows | Source |
|---|---|---|
| `hero_hollow_profile_2026-10-22.jpg` and the five other `*_2026-10-22.jpg` frames | In-game frames | The game's scripted `-physxr-capture` tour of 22 Oct 2026 (desktop, not a headset) |
| `menu_sensor_panel_2026-10-24.jpg` | The menu's sensor panel with no station plugged in | The menu's capture mode, 24 Oct 2026 |
| `station_flow.svg` | Sensor station data flow: ToF and MPU6050 → ESP32 → USB serial → Unity validation → `WorldState` | Drawn from `Firmware/physxr_station/physxr_station.ino` (pins, I²C addresses, rates, baud) |
| `hardware_schematic_poster_2025-05-25.jpg` | The team's hardware schematic poster | Used by [`Firmware/README.md`](../../Firmware/README.md) |

Older captures from before the 22 Oct presentation pass (`hero_corridor_follow.jpg`, `first_sighting_e5.jpg`, `kitchen_fuse_objective.jpg`, `encounter_lantern_prompt.jpg`, `power_restored_surge.jpg`, `ending_window_silhouette.jpg`, `menu_sensor_condition.jpg`) are no longer linked from any document.
