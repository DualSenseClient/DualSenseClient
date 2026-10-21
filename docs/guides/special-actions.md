# Special Actions

Special Actions let you trigger custom effects by holding a **button combination** or swiping on the **touchpad**. Each action is global, is enabled per controller, and can do things like disconnect the controller, change lights, play a sound, or show the battery level.

## Where to Find Them

Open **Customization** and expand **Special Actions**. All actions are global — you create them once and then enable them per-controller.

![Special Actions section on the Customization page](../assets/images/special-actions.png)

## Creating an Action

1. Press **+** in the Special Actions header to create an action.
2. Give it a name.
3. Toggle **Enabled for this controller** to enable it for the currently selected controller. An action with no enabled controllers is defined but never fires.
4. Configure the trigger and effects described below.

Use the **Import** / **Export** buttons in the header to share whole collections. To export a single action, open it and use the export button next to its name. Imported actions arrive disabled — enable them for the controllers you want.

![Expanded special action editor with trigger combo and effects](../assets/images/special-actions-editor.png)

## Trigger

Each action has exactly one trigger:

| Trigger | Description |
| ------- | ----------- |
| **Button combination** | Hold one or more buttons simultaneously. The combination must match exactly — holding an extra button blocks it. It fires once and re-arms when you release the buttons. |
| **Touchpad gesture** | Swipe a single finger across the touchpad in one of four directions: **Swipe Up**, **Swipe Down**, **Swipe Left**, **Swipe Right**. When a gesture is set, the button combination is ignored. |

## Timing

| Setting | Range | Description |
| ------- | ----- | ----------- |
| **Hold time** | 0 – 10 s | How long the exact combination (or swipe) must be held before the action fires. `0` fires immediately. |
| **Apply while held** | on/off | When enabled, light effects revert and sound stops as soon as you release the trigger, instead of staying applied. |
| **Show duration** | 0 – 60 s | How long light effects stay applied before the assigned profile is restored automatically. `0` keeps them applied. Ignored for Disconnect/Sound and when *Apply while held* is on. |

## Effects

An action can carry several effects at once, at most one per type. Pick the ones you need:

| Effect | What it does |
| ------ | ------------ |
| **Disconnect** | Disconnects the controller over Bluetooth. |
| **Set lightbar color** | Sets the lightbar to a chosen RGB color. Uses the color picker or Red/Green/Blue sliders. |
| **Set player LEDs** | Sets the player LED layout — either a preset (Player 1 – 5) or individual LED 1 – 5 toggles. On hardware generations that do not report full LED support, only mirrored presets are available. |
| **Play sound** | Plays an audio file (mp3, wav, flac, …) through the controller's **Speaker** or a **Headset** plugged into the controller jack. Configurable volume (0 – 255), and optional haptic feedback with strength (0 – 200%). |
| **Show battery level** | Shows the current charge on the **lightbar** (10 colors, one per 10% band), on the **player LEDs** as five gates (Generation 2/3 only), and/or in a desktop notification. Choose a color per lightbar level; missing entries fall back to the default gradient from red (low) through orange/yellow to green (full). |

!!! note
    Disabling an individual effect keeps it in the list with its parameters — toggling it back on restores them without re-entering values.

!!! warning "Battery effect combinations"
    While **Show battery level** is set to use the lightbar, it cannot be combined with **Set lightbar color** on the same action. While it uses the player LEDs, it cannot be combined with **Set player LEDs**. A notification-only battery effect can be combined with light effects.

## Tips

- Use a lightbar + player-LED action to give yourself a visual mode indicator (e.g. a red lightbar for a "stealth" profile).
- Use a short sound action as a confirmation beep when you press a combo that changes lights.
- A notification-only battery action can sit alongside a light effect, giving you both the visual change and the charge readout.
- Export your actions before reinstalling or resetting settings — the whole collection lives in a single settings file.
