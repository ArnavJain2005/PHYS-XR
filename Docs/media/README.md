# Media inventory

Images used by the [main README](../../README.md) and the docs.

- **Present:** everything listed as present was checked as a file in this folder (26 Oct 2026). Every one is a real capture or a diagram drawn from the code; none is a mock-up.
- **Missing:** nothing listed as missing exists yet. The README marks each missing item with a visible **TODO(media)** note naming the exact file. Those notes must be replaced with the real media, or removed with the item kept here as outstanding, before merge.

## Present

### The date-stamped desktop captures

Six in-game frames from the game's scripted `-physxr-capture` tour on 22 Oct 2026, plus the menu frame from 24 Oct. All are desktop captures from local Windows builds, not headset recordings.

| File | Size | Dimensions | Shows | Used by |
|---|---|---|---|---|
| `hero_hollow_profile_2026-10-22.jpg` | 48,209 B | 1280×720 | The Hollow in a doorway, lantern light | README hero |
| `first_sighting_corridor_2026-10-22.jpg` | 32,495 B | 960×540 | First sighting in the corridor (rule E5) | README, gallery |
| `encounter_dining_hall_2026-10-22.jpg` | 29,232 B | 960×540 | The encounter in the dining hall | README, gallery |
| `entry_hall_moonlight_2026-10-22.jpg` | 30,384 B | 960×540 | Entry hall, moonlight and emergency lamp | Gallery |
| `power_restored_utility_2026-10-22.jpg` | 46,344 B | 960×540 | Utility room after the power is restored (rule R2) | Gallery |
| `ending_window_silhouette_2026-10-22.jpg` | 22,584 B | 960×540 | The ending: a figure in the window | Gallery |
| `menu_sensor_panel_2026-10-24.jpg` | 76,616 B | 960×540 | The menu's sensor panel with no station plugged in (menu capture mode, 24 Oct 2026) | README, gallery |

"Gallery" means [`HORROR_PRESENTATION.md`](../HORROR_PRESENTATION.md#the-game-and-gallery).

### Diagrams and other files

| File | Size | Shows | Used by |
|---|---|---|---|
| `station_flow.svg` | 5,058 B | Station data flow for proximity and knocks: ToF and MPU6050 → ESP32 → USB serial → Unity validation → `WorldState`, drawn from the pins, I²C addresses, rates and baud in `physxr_station.ino` | [`ARCHITECTURE.md`](../ARCHITECTURE.md#station-data-flow) |
| `hardware_schematic_poster_2025-05-25.jpg` | 360,628 B | The team's hardware schematic poster | [`Firmware/README.md`](../../Firmware/README.md) |

### Older captures, kept but not linked

These are from before the 22 Oct presentation pass and no document links to them:

| File | Size |
|---|---|
| `hero_corridor_follow.jpg` | 38,433 B |
| `first_sighting_e5.jpg` | 25,558 B |
| `kitchen_fuse_objective.jpg` | 40,699 B |
| `encounter_lantern_prompt.jpg` | 28,623 B |
| `power_restored_surge.jpg` | 44,395 B |
| `ending_window_silhouette.jpg` | 23,690 B |
| `menu_sensor_condition.jpg` | 32,027 B |

## Missing (not yet recorded)

| Target file | Purpose | Proposed size or duration | Status |
|---|---|---|---|
| `physxr-physical-loop-demo.mp4` | Split screen. Left: the real room, someone leaning in to the station and knocking it. Right: the game at the same moment, the ghost turning and walking off to search. One continuous take with the F1 overlay visible | 60–90 s, 1920×1080, ≥ 30 fps | Missing; README TODO(media) at the top |
| `the-presence-gameplay.gif` | A short clip of ordinary play: the corridor, the first-sighting lunge, the lantern stagger | 8–12 s, about 800 px wide, under 10 MB | Missing; README TODO(media) under the hero |
| `physxr-sensor-station.jpg` | A genuine photograph of the assembled station as it is, connected to the PC | About 1600×1067 JPEG | Missing; README TODO(media) in "The sensor station" |
| `quest-link-demo.mp4` | A genuine PC VR recording over Quest Link (Quest 2 or Quest 3), with the station driving a reaction | 20–40 s, the headset's capture resolution | Missing; README TODO(media) in "Status and roadmap" |
| `social-preview-1200x630.png` | GitHub social preview: the hero frame with the project name | 1200×630 PNG, under 1 MB | Missing; set by the owner in repository settings (Settings → General → Social preview), not linked from the README |

GitHub does not play a committed `.mp4` inline in a README. For the two videos, also upload the file through an issue or PR comment (or attach it to a Release), and link a poster frame to that URL.
