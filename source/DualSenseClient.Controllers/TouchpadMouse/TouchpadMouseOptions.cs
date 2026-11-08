namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// Immutable settings that drive a <see cref="TouchpadMouseProcessor"/>.
/// </summary>
/// <param name="Enabled">Whether the touchpad mouse feature is enabled.</param>
/// <param name="PointerSpeed">Multiplier applied to pointer movement (UI range 0.25–3.0).</param>
/// <param name="ScrollSpeed">Multiplier applied to wheel deltas (UI range 0.25–3.0).</param>
/// <param name="NaturalScrolling">Whether content follows the fingers instead of the Windows-style direction.</param>
public sealed record TouchpadMouseOptions(bool Enabled, double PointerSpeed, double ScrollSpeed, bool NaturalScrolling);