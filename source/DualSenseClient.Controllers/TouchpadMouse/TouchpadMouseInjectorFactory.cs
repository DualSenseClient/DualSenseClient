using DualSenseClient.Controllers.TouchpadMouse.Windows;

namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// Creates the mouse injector for the current platform: the Win32 <c>SendInput</c>
/// implementation on Windows, and an inert <see cref="NullMouseInjector"/> elsewhere.
/// </summary>
public static class TouchpadMouseInjectorFactory
{
    /// <summary>
    /// Whether the current platform supports mouse injection.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            return OperatingSystem.IsWindows();
        }
    }

    /// <summary>
    /// Creates a mouse injector for the current platform.
    /// </summary>
    /// <returns>A <see cref="SendInputMouseInjector"/> on Windows; otherwise a <see cref="NullMouseInjector"/>.</returns>
    public static IMouseInjector Create() => IsSupported ? new SendInputMouseInjector() : new NullMouseInjector();
}