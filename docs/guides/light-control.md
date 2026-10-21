# Light Control

DualSense Client gives you full control over the controller's lighting: the RGB lightbar, the five player LEDs, and the microphone LED. The controls live in the profile editor under **Customization → Profiles**.

## Lightbar

The lightbar supports full RGB color control:

- **Color picker** — pick a color visually, with a spectrum and a palette of standard colors
- **Sliders** — fine-tune the red, green, and blue channels
- **Profile color swatch** — the selected profile's color is shown in the profile list and dropdown

![Lightbar controls in the profile editor](../assets/images/light-control.png)

!!! tip
    Put the look you want in a [profile](profiles.md) and assign it to the controller on the Device Info page, so it is applied automatically whenever that controller connects.

## Player LEDs

Toggle each of the five player indicator LEDs individually, or use the **Player 1 – 5** presets. This is the same set of lights used on the PS5 to show player slots (1–4 plus the extra LED).

![Player LED toggles in the profile editor](../assets/images/player-leds.png)

!!! note "Depends on controller generation"
    The available controls depend on the connected controller's hardware generation (see [Firmware & Hardware](device-info.md#firmware-hardware)):

    - **Generation 0x02 / 0x03** — full support. The profile shows both the five player presets (**Player 1 – 5**) and five individual toggles (**LED 1 – 5**).
    - **Generation 0x04** — **mirrored only**. Only the five presets are offered; the individual LED toggles are hidden. You can verify the generation on the Device Information page.
    - When no controller is connected or its firmware info is not yet valid, the view falls back to presets only.

## Microphone LED

Configure how the microphone LED behaves:

| Mode  | Behavior                        |
| ----- | ------------------------------- |
| Off   | The mic LED stays off           |
| On    | The mic LED stays lit           |
| Pulse | The mic LED pulses continuously |

---

Edits to the profile assigned to the selected controller are applied immediately. Profiles are saved automatically — assign one on the [Device Info](device-info.md) page to apply it on connection.
