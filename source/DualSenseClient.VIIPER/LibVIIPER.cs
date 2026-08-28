using System.Runtime.InteropServices;
using DualSenseClient.VIIPER.Callbacks;
using DualSenseClient.VIIPER.DualSense;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Keyboard;
using DualSenseClient.VIIPER.Mouse;
using DualSenseClient.VIIPER.NS2Pro;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.VIIPER;

/// <summary>
/// P/Invoke interop for the libVIIPER native library
/// (Windows: libVIIPER.dll, Linux: libVIIPER.so).
/// Every method returns 1 on success and 0 on failure.
/// </summary>
public static class LibVIIPER
{
    /// <summary>
    /// Name of the native library, resolved by <see cref="NativeLibraryResolver"/>
    /// to the binary embedded in this assembly.
    /// </summary>
    private const string Library = "libVIIPER";

    /// <summary>
    /// Registers <see cref="NativeLibraryResolver"/> before any P/Invoke call so the
    /// runtime resolves "libVIIPER" to the embedded native binary.
    /// </summary>
    static LibVIIPER()
    {
        NativeLibraryResolver.Register();
    }

    /// <summary>
    /// Release tag of the embedded libVIIPER native library (e.g. "dev-snapshot"),
    /// or null if no version information was embedded.
    /// </summary>
    public static string? NativeLibraryVersion
    {
        get
        {
            return NativeLibraryResolver.NativeVersion;
        }
    }

    /// <summary>
    /// Creates a new USB server running in the background.
    /// The returned handle must be released with <see cref="CloseUSBServer"/>.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <param name="outHandle">Output parameter for the created server handle.</param>
    /// <param name="logCallback">Optional callback for server log messages.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool NewUSBServer([In] ref USBServerConfig config, out nuint outHandle, VIIPERLogCallback? logCallback);

    /// <summary>
    /// Closes the USB server, automatically removing all of its busses and devices.
    /// </summary>
    /// <param name="handle">Handle to the USB server to close.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CloseUSBServer(nuint handle);

    /// <summary>
    /// Creates a new USB bus on the server.
    /// Pass 0 as <paramref name="busID"/> to have the server assign the next free bus ID.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="busID">ID of the bus to create, or 0 to let the server assign one.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateUSBBus(nuint serverHandle, ref uint busID);

    /// <summary>
    /// Removes the USB bus with the given ID and all devices associated with it.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="busID">ID of the bus to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveUSBBus(nuint serverHandle, uint busID);

    // ── DualSense ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new DualSense (non-edge) device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a new DualSense Edge device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseEdgeDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a DualSense exposing only the audio interfaces and no HID gamepad interface.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseAudioOnlyDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a DualSense Edge exposing only the audio interfaces and no HID gamepad interface.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseEdgeAudioOnlyDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a DualSense exposing only the HID gamepad interface.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseGamepadOnlyDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a DualSense Edge exposing only the HID gamepad interface.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseEdgeGamepadOnlyDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta);

    /// <summary>
    /// Creates a DualSense family device selected by a registered device type name.
    /// In addition to the classic variants this reaches the events and raw-input aliases
    /// (e.g. "dualsensecombinedaudioduplexv5rawinputevents", case-insensitive).
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    /// <param name="deviceType">Registered DualSense device type name (case-insensitive).</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDualSenseDeviceByType(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, DSMetaState[]? meta,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceType);

    /// <summary>
    /// Updates the input state of the DualSense device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="state">New input state to set on the device.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseDeviceState(nuint deviceHandle, DSDeviceState state);

    /// <summary>
    /// Updates the input state together with physical raw-input metadata, mirroring the
    /// 53-byte V5RawInput wire payload as one atomic unit.
    /// Pass a zero-initialized <paramref name="raw"/> (Valid = 0) to behave exactly like
    /// <see cref="SetDualSenseDeviceState"/>.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="state">New input state to set on the device.</param>
    /// <param name="raw">Physical raw-input metadata accompanying the state.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseDeviceStateRaw(nuint deviceHandle, DSDeviceState state, ref DSRawInputMetadata raw);

    /// <summary>
    /// Updates the meta (identity/battery/sensor) state at runtime.
    /// Fields left at their zero value keep the current value.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="meta">Updated metadata, or null to change nothing.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseMetaState(nuint deviceHandle, DSMetaState[]? meta);

    /// <summary>
    /// Sets a callback invoked when the host sends output (rumble/LED) commands to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback receiving rumble and LED values, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseOutputCallback(nuint deviceHandle, DSOutputCallback? callback);

    /// <summary>
    /// Sets a callback delivering the full output state, including adaptive trigger blocks.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback receiving the full output state, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseOutputStateCallback(nuint deviceHandle, DSOutputStateCallback? callback);

    /// <summary>
    /// Sets a low-latency haptics callback invoked when a rear haptics interval completes.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback receiving the full output state, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseRealtimeHapticsCallback(nuint deviceHandle, DSRealtimeHapticsCallback? callback);

    /// <summary>
    /// Sets a callback invoked once per 480-frame speaker generation of the V5 transport.
    /// Each invocation pairs the native feedback output state with exactly that generation's
    /// speaker PCM: two S16LE channels (front stereo) at 48 kHz, 1920 bytes.
    /// The buffer is only valid during the call.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback receiving the full output state and its paired PCM buffer, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseAtomicAudioHapticsCallback(nuint deviceHandle, DSAtomicAudioHapticsCallback? callback);

    /// <summary>
    /// Sets a callback invoked when the haptics audio interface is reset or its alternate setting changes.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback with no arguments, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseSpeakerResetCallback(nuint deviceHandle, DSSpeakerResetCallback? callback);

    /// <summary>
    /// Sets a callback invoked when the host sends haptics/speaker PCM to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="callback">Callback receiving the PCM buffer (valid only during the call), or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseAudioOutCallback(nuint deviceHandle, DSAudioCallback? callback);

    /// <summary>
    /// Queues a microphone PCM frame captured from the host-facing mic stream.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device.</param>
    /// <param name="data">PCM frame; must be exactly 1920 bytes.</param>
    /// <param name="length">Length of the PCM frame.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDualSenseMicrophonePCM(nuint deviceHandle, byte[] data, nuint length);

    /// <summary>
    /// Removes the DualSense device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DualSense device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveDualSenseDevice(nuint deviceHandle);

    // ── DualShock 4 ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new DualShock 4 device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateDS4Device(nuint serverHandle, out nuint outDeviceHandle, uint busID, [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost,
        ushort idVendor, ushort idProduct, DS4MetaState[]? meta);

    /// <summary>
    /// Updates the input state of the DualShock 4 device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="state">New input state to set on the device.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4DeviceState(nuint deviceHandle, DS4DeviceState state);

    /// <summary>
    /// Sets a callback invoked when the host sends output (rumble/LED) commands to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="callback">Callback receiving rumble, LED and flash values, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4OutputCallback(nuint deviceHandle, DS4OutputCallback? callback);

    /// <summary>
    /// Sets a callback invoked when the host sends speaker PCM to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="callback">Callback receiving the PCM buffer (valid only during the call), or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4SpeakerCallback(nuint deviceHandle, DS4SpeakerCallback? callback);

    /// <summary>
    /// Sets a callback invoked when the speaker audio interface is reset or its alternate setting changes.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="callback">Callback with no arguments, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4SpeakerResetCallback(nuint deviceHandle, DS4SpeakerResetCallback? callback);

    /// <summary>
    /// Queues a microphone PCM frame captured from the host-facing mic stream.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="data">PCM frame; must be exactly 320 bytes.</param>
    /// <param name="length">Length of the PCM frame.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4MicrophonePCM(nuint deviceHandle, byte[] data, nuint length);

    /// <summary>
    /// Updates the meta (identity/battery) state at runtime.
    /// Fields left at their zero value keep the current value.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device.</param>
    /// <param name="meta">Updated metadata, or null to change nothing.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetDS4MetaState(nuint deviceHandle, DS4MetaState[]? meta);

    /// <summary>
    /// Removes the DualShock 4 device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the DS4 device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveDS4Device(nuint deviceHandle);

    // ── Keyboard ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new HID keyboard device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateKeyboardDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct);

    /// <summary>
    /// Updates the input state of the keyboard device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the keyboard device.</param>
    /// <param name="state">New input state (modifiers bitmask + 256-bit key bitmap).</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetKeyboardDeviceState(nuint deviceHandle, KeyboardDeviceState state);

    /// <summary>
    /// Sets a callback invoked when the host changes keyboard LED state.
    /// </summary>
    /// <param name="deviceHandle">Handle to the keyboard device.</param>
    /// <param name="callback">Callback receiving the raw LED bitmask byte, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetKeyboardLEDCallback(nuint deviceHandle, KeyboardLEDCallback? callback);

    /// <summary>
    /// Removes the keyboard device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the keyboard device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveKeyboardDevice(nuint deviceHandle);

    // ── Mouse ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new HID mouse device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateMouseDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct);

    /// <summary>
    /// Updates the input state of the mouse device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the mouse device.</param>
    /// <param name="state">New input state. DX/DY/Wheel/Pan are relative and consumed each poll cycle.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetMouseDeviceState(nuint deviceHandle, MouseDeviceState state);

    /// <summary>
    /// Removes the mouse device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the mouse device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveMouseDevice(nuint deviceHandle);

    // ── Nintendo Switch 2 Pro ────────────────────────────────────────

    /// <summary>
    /// Creates a new Nintendo Switch 2 Pro Controller device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="meta">Optional initial device metadata, or null to use defaults.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateNS2ProDevice(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, NS2ProMetaState[]? meta);

    /// <summary>
    /// Updates the input state of the NS2Pro device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the NS2Pro device.</param>
    /// <param name="state">New input state to set on the device.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetNS2ProDeviceState(nuint deviceHandle, NS2ProDeviceState state);

    /// <summary>
    /// Updates the meta (identity/battery) state at runtime.
    /// Fields left at their zero value keep the current value.
    /// </summary>
    /// <param name="deviceHandle">Handle to the NS2Pro device.</param>
    /// <param name="meta">Updated metadata, or null to change nothing.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetNS2ProMetaState(nuint deviceHandle, NS2ProMetaState[]? meta);

    /// <summary>
    /// Sets a callback invoked when the host sends output (rumble/LED) commands to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the NS2Pro device.</param>
    /// <param name="callback">Callback receiving the full output state, or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetNS2ProOutputCallback(nuint deviceHandle, NS2ProOutputCallback? callback);

    /// <summary>
    /// Removes the NS2Pro device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the NS2Pro device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveNS2ProDevice(nuint deviceHandle);

    // ── Xbox 360 ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new Xbox360 device on the given bus.
    /// </summary>
    /// <param name="serverHandle">Handle to the USB server.</param>
    /// <param name="outDeviceHandle">Output parameter for the created device handle.</param>
    /// <param name="busID">ID of the bus to add the device to.</param>
    /// <param name="autoAttachLocalhost">If true, automatically attach to the USBIP client on this machine.</param>
    /// <param name="idVendor">Optional USB vendor ID (0 = default).</param>
    /// <param name="idProduct">Optional USB product ID (0 = default).</param>
    /// <param name="xinputSubType">Optional XInput subtype (0x01 gamepad, 0x02 wheel, etc.).</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CreateXbox360Device(nuint serverHandle, out nuint outDeviceHandle, uint busID,
        [MarshalAs(UnmanagedType.I1)] bool autoAttachLocalhost, ushort idVendor, ushort idProduct, byte xinputSubType);

    /// <summary>
    /// Updates the input state of the Xbox360 device associated with the given handle.
    /// </summary>
    /// <param name="deviceHandle">Handle to the Xbox360 device.</param>
    /// <param name="state">New input state to set on the device.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetXbox360DeviceState(nuint deviceHandle, Xbox360DeviceState state);

    /// <summary>
    /// Sets a callback invoked when the host sends rumble/motor commands to the device.
    /// </summary>
    /// <param name="deviceHandle">Handle to the Xbox360 device.</param>
    /// <param name="callback">Callback receiving left/right motor intensities (0-255), or null to clear.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetXbox360RumbleCallback(nuint deviceHandle, Xbox360RumbleCallback? callback);

    /// <summary>
    /// Removes the Xbox360 device associated with the given handle from the server.
    /// </summary>
    /// <param name="deviceHandle">Handle to the Xbox360 device to remove.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool RemoveXbox360Device(nuint deviceHandle);
}