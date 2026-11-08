# Media inventory

Images used by the [main README](../../README.md) and the docs.

- **Present:** everything listed as present was checked as a file in this folder. Every one is a real capture or a diagram drawn from the code; none is a mock-up.
- **Missing / unrecorded:** items that have not been recorded or cannot be captured. Visible `TODO(media)` placeholders have been removed from the main README so only authentic, verified assets and captures are presented.

## Present

### The date-stamped desktop captures

Six in-game frames from the game's scripted `-physxr-capture` tour on 22 Oct 2026, plus the menu frame from 24 Oct. All are desktop captures from local Windows builds, not headset recordings.

| File | Size | Dimensions | Shows | Used by |
|---|---|---|---|---|
| `hero_hollow_profile_2026-10-22.jpg` | 48,209 B | 1280×720 | The Hollow in a doorway, lantern light | README hero |
| `first_sighting_corridor_2026-10-22.jpg` | 32,495 B | 960×540 | First sighting in the corridor (rule E5) | README, gallery |
| `encounter_dining_hall_2026-10-22.jpg` | 29,232 B | 960×540 | The encounter in the dining hall | README, gallery |
| `entry_hall_moonlight_2026-10-22.jpg` | 30,384 B | 960×540 | Entry hall, moonlight and emergency lamp | README, gallery |
| `power_restored_utility_2026-10-22.jpg` | 46,344 B | 960×540 | Utility room after the power is restored (rule R2) | Gallery |
| `ending_window_silhouette_2026-10-22.jpg` | 22,584 B | 960×540 | The ending: a figure in the window | Gallery |
| `menu_sensor_panel_2026-10-24.jpg` | 76,616 B | 960×540 | The menu's sensor panel with no station plugged in (menu capture mode, 24 Oct 2026) | Gallery |

"Gallery" means [`HORROR_PRESENTATION.md`](../HORROR_PRESENTATION.md#the-game-and-gallery).

### Diagrams, title animation, and other files

| File | Size | Shows | Used by |
|---|---|---|---|
| `physxr-title.gif` | 1,302,831 B | Animated title reveal GIF: ghost drifting over elegant italic title | Main [`README.md`](../../README.md) title |
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

## Unrecorded or unavailable items

| Target file | Purpose | Proposed size or duration | Status |
|---|---|---|---|
| `physxr-physical-loop-demo.mp4` | Split screen: real room leaning/knocking station alongside in-game ghost reaction | 60–90 s, 1920×1080, ≥ 30 fps | Unavailable: physical ESP32 station is lost |
| `the-presence-gameplay.gif` | Short animated clip of ordinary gameplay: corridor, first-sighting lunge, lantern stagger | 8–12 s, ~800 px wide | Unrecorded in current headless environment; README presentation completed using genuine date-stamped screenshots |
| `physxr-sensor-station.jpg` | Photograph of the assembled station connected to PC | ~1600×1067 JPEG | Unavailable: physical ESP32 station is lost |
| `quest-link-demo.mp4` | PC VR recording over Quest Link | 20–40 s, headset capture resolution | Station-driven recording unavailable due to lost hardware; standalone VR gameplay recording without hardware remains a software roadmap item |
| `social-preview-1200x630.png` | GitHub social preview: hero frame with project name | 1200×630 PNG, under 1 MB | Outstanding: set by owner in GitHub repository settings, not linked in README |
