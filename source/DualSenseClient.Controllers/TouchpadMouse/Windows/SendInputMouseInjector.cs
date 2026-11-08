using System.Runtime.InteropServices;

namespace DualSenseClient.Controllers.TouchpadMouse.Windows;

/// <summary>
/// <see cref="IMouseInjector"/> backed by the Win32 <c>user32!SendInput</c> API. Events use
/// relative mouse movement, so injection never depends on the cursor's absolute position.
/// The instance reuses one single-entry <c>INPUT</c> buffer and is not safe to share between
/// threads; each controller owns its injector and calls it from the controller's read thread.
/// </summary>
public sealed class SendInputMouseInjector : IMouseInjector
{
    /// <summary><c>INPUT_MOUSE</c>: the event describes a mouse.</summary>
    private const uint INPUT_MOUSE = 0;

    /// <summary><c>MOUSEEVENTF_MOVE</c>: relative pointer movement.</summary>
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    /// <summary><c>MOUSEEVENTF_LEFTDOWN</c>: left button press.</summary>
    private const uint LEFTDOWN = 0x0002;

    /// <summary><c>MOUSEEVENTF_LEFTUP</c>: left button release.</summary>
    private const uint LEFTUP = 0x0004;

    /// <summary><c>MOUSEEVENTF_RIGHTDOWN</c>: right button press.</summary>
    private const uint RIGHTDOWN = 0x0008;

    /// <summary><c>MOUSEEVENTF_RIGHTUP</c>: right button release.</summary>
    private const uint RIGHTUP = 0x0010;

    /// <summary><c>MOUSEEVENTF_WHEEL</c>: vertical wheel movement carried in <c>MouseData</c>.</summary>
    private const uint WHEEL = 0x0800;

    /// <summary><c>MOUSEEVENTF_HWHEEL</c>: horizontal wheel movement carried in <c>MouseData</c>.</summary>
    private const uint HWHEEL = 0x1000;

    /// <summary>Single-entry event buffer reused for every injection.</summary>
    private readonly INPUT[] _buffer = new INPUT[1];

    /// <inheritdoc/>
    public bool Move(int dx, int dy) => Send(MOUSEEVENTF_MOVE, dx, dy, 0);

    /// <inheritdoc/>
    public bool LeftDown() => Send(LEFTDOWN, 0, 0, 0);

    /// <inheritdoc/>
    public bool LeftUp() => Send(LEFTUP, 0, 0, 0);

    /// <inheritdoc/>
    public bool RightDown() => Send(RIGHTDOWN, 0, 0, 0);

    /// <inheritdoc/>
    public bool RightUp() => Send(RIGHTUP, 0, 0, 0);

    /// <inheritdoc/>
    public bool Wheel(int delta) => Send(WHEEL, 0, 0, delta);

    /// <inheritdoc/>
    public bool HorizontalWheel(int delta) => Send(HWHEEL, 0, 0, delta);

    /// <summary>
    /// Fills the reused buffer with one mouse event and injects it.
    /// </summary>
    /// <param name="flags">The <c>MOUSEEVENTF_*</c> flags of the event.</param>
    /// <param name="dx">Relative horizontal movement.</param>
    /// <param name="dy">Relative vertical movement.</param>
    /// <param name="mouseData">Wheel delta for wheel events; 0 otherwise.</param>
    /// <returns><see langword="true"/> when Windows inserted the event.</returns>
    private bool Send(uint flags, int dx, int dy, int mouseData)
    {
        ref MOUSEINPUT mouse = ref _buffer[0].U.Mi;
        mouse.Dx = dx;
        mouse.Dy = dy;
        mouse.MouseData = mouseData;
        mouse.DwFlags = flags;
        mouse.Time = 0;
        mouse.DwExtraInfo = 0;
        _buffer[0].Type = INPUT_MOUSE;

        return SendInput(1, _buffer, Marshal.SizeOf<INPUT>()) == 1;
    }

    /// <summary>
    /// One synthetic input event. The union is explicit so the mouse payload starts at
    /// offset 0; <c>cbSize</c> is 40 bytes on x64.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        /// <summary>The event type; <c>INPUT_MOUSE</c> for mouse events.</summary>
        public uint Type;

        /// <summary>The event payload.</summary>
        public InputUnion U;
    }

    /// <summary>Payload union of <see cref="INPUT"/>; only the mouse member is used.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        /// <summary>The mouse event payload.</summary>
        [FieldOffset(0)] public MOUSEINPUT Mi;
    }

    /// <summary>Win32 <c>MOUSEINPUT</c>: relative movement, button, or wheel event.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        /// <summary>Relative horizontal movement.</summary>
        public int Dx;

        /// <summary>Relative vertical movement.</summary>
        public int Dy;

        /// <summary>Wheel delta for wheel events; 0 otherwise.</summary>
        public int MouseData;

        /// <summary>The <c>MOUSEEVENTF_*</c> flags of the event.</summary>
        public uint DwFlags;

        /// <summary>Event timestamp; 0 lets the system supply it.</summary>
        public uint Time;

        /// <summary>Application-specific extra info; unused.</summary>
        public nint DwExtraInfo;
    }

    /// <summary>
    /// Inserts synthetic input events into the system input stream.
    /// </summary>
    /// <param name="nInputs">Number of events in <paramref name="pInputs"/>.</param>
    /// <param name="pInputs">The event array.</param>
    /// <param name="cbSize">Size of one <c>INPUT</c> structure in bytes.</param>
    /// <returns>The number of events inserted; 0 when the foreground window blocks injection.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}