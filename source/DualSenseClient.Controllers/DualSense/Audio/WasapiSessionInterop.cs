using System.Runtime.InteropServices;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Minimal WASAPI device/session interop used to list the applications currently
/// rendering audio on the default output device. Only usable on Windows; the caller
/// checks that first.
/// </summary>
internal static class WasapiSessionInterop
{
    /// <summary>
    /// <c>IID_IMMDeviceEnumerator</c> ({A95664D2-9614-4F35-A746-DE8DB63617E6}).
    /// </summary>
    private static readonly Guid DeviceEnumeratorIid = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");

    /// <summary>
    /// <c>CLSID_MMDeviceEnumerator</c> ({BCDE0395-E52F-467C-8E3D-C4579291692E}).
    /// </summary>
    private static readonly Guid DeviceEnumeratorClsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>
    /// <c>IID_IAudioSessionManager2</c> ({77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F}).
    /// </summary>
    private static readonly Guid AudioSessionManager2Iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    /// <summary>
    /// <c>EDataFlow.eRender</c>.
    /// </summary>
    private const int DataFlowRender = 0;

    /// <summary>
    /// <c>ERole.eConsole</c>.
    /// </summary>
    private const int RoleConsole = 0;

    /// <summary>
    /// <c>CLSCTX_INPROC_SERVER</c>: the execution context the audio services expect.
    /// </summary>
    private const int ClsCtxInProcServer = 0x1;

    /// <summary>
    /// Returns the process ids that currently have an audio session on the default
    /// render device. Returns an empty set when the platform is unsupported or the
    /// enumeration fails; failures are logged so an empty picker can be told apart from
    /// "nothing is playing".
    /// </summary>
    /// <param name="logWarning">Receives a message and exception when the enumeration fails.</param>
    /// <param name="logDebug">Receives a message with the session count on success.</param>
    public static HashSet<int> GetRenderSessionProcessIds(Action<string, Exception?> logWarning, Action<string> logDebug)
    {
        HashSet<int> processIds = new HashSet<int>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (CreateDeviceEnumerator(logWarning) is not { } deviceEnumerator)
            {
                return processIds;
            }

            enumerator = deviceEnumerator;
            int endpointHr = enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out device);
            if (endpointHr < 0 || device is null)
            {
                logDebug(
                    $"GetDefaultAudioEndpoint returned HRESULT 0x{endpointHr:X8}{(device is null ? " and no endpoint" : string.Empty)}; no render device is available");
                return processIds;
            }

            Guid managerIid = AudioSessionManager2Iid;
            int activateHr = device.Activate(ref managerIid, ClsCtxInProcServer, IntPtr.Zero, out managerObject);
            if (activateHr < 0 || managerObject is not IAudioSessionManager2 sessionManager)
            {
                string type = managerObject is null ? "null" : managerObject.GetType().FullName ?? "unknown";
                logWarning(
                    $"IMMDevice::Activate(IAudioSessionManager2) failed: HRESULT 0x{activateHr:X8}, result type {type}; the application list will be empty",
                    null);
                return processIds;
            }

            manager = sessionManager;
            int enumeratorHr = manager.GetSessionEnumerator(out sessions);
            if (enumeratorHr < 0 || sessions is null)
            {
                logWarning(
                    $"IAudioSessionManager2::GetSessionEnumerator failed: HRESULT 0x{enumeratorHr:X8}; the application list will be empty",
                    null);
                return processIds;
            }

            int countHr = sessions.GetCount(out int count);
            if (countHr < 0)
            {
                logWarning(
                    $"IAudioSessionEnumerator::GetCount failed: HRESULT 0x{countHr:X8}; the application list will be empty",
                    null);
                return processIds;
            }

            for (int i = 0; i < count; i++)
            {
                object? session = null;
                try
                {
                    int sessionHr = sessions.GetSession(i, out session);
                    if (sessionHr < 0 || session is not IAudioSessionControl2 control)
                    {
                        logDebug(
                            $"Skipping audio session {i}: GetSession returned HRESULT 0x{sessionHr:X8}{(session is null ? " and no object" : $", type {session.GetType().FullName}")}");
                        continue;
                    }

                    // IsSystemSoundsSession returns S_OK (0) for the system-sounds
                    // session, S_FALSE (1) otherwise.
                    if (control.IsSystemSoundsSession() == 0)
                    {
                        continue;
                    }

                    int processHr = control.GetProcessId(out uint processId);
                    if (processHr < 0 || processId == 0)
                    {
                        logDebug($"Skipping audio session {i}: GetProcessId returned HRESULT 0x{processHr:X8} and PID {processId}");
                        continue;
                    }

                    processIds.Add((int)processId);
                }
                finally
                {
                    ReleaseComObject(session);
                }
            }

            logDebug($"Audio session enumeration found {count} session(s)");
        }
        catch (Exception ex)
        {
            logWarning("Enumerating audio sessions failed; the application list will be empty", ex);
        }
        finally
        {
            ReleaseComObject(sessions);
            ReleaseComObject(manager);
            ReleaseComObject(managerObject);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }

        return processIds;
    }

    /// <summary>
    /// Creates the default audio endpoint enumerator, or <c>null</c> when the COM class
    /// is unavailable. Failures are reported through <paramref name="logWarning"/> with
    /// the failing HRESULT so an empty picker can be diagnosed.
    /// </summary>
    /// <param name="logWarning">Receives a failure message and optional exception.</param>
    private static IMMDeviceEnumerator? CreateDeviceEnumerator(Action<string, Exception?> logWarning)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        Guid clsid = DeviceEnumeratorClsid;
        Guid iid = DeviceEnumeratorIid;
        object? instance = null;
        try
        {
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtxInProcServer, ref iid, out instance);
            if (hr < 0)
            {
                ReleaseComObject(instance);
                logWarning(
                    $"CoCreateInstance(MMDeviceEnumerator) failed: HRESULT 0x{hr:X8}; the application list will be empty",
                    null);
                return null;
            }

            // The returned RCW is the caller's to release: releasing it here would hand
            // back a detached RCW whose every call throws InvalidComObjectException.
            IMMDeviceEnumerator? enumerator = instance as IMMDeviceEnumerator;
            if (enumerator is null)
            {
                string type = instance is null ? "null" : instance.GetType().FullName ?? "unknown";
                ReleaseComObject(instance);
                logWarning(
                    $"CoCreateInstance(MMDeviceEnumerator) returned an unexpected type {type}; the application list will be empty",
                    null);
            }

            return enumerator;
        }
        catch (Exception ex)
        {
            ReleaseComObject(instance);
            logWarning("Creating the audio device enumerator failed; the application list will be empty", ex);
            return null;
        }
    }

    /// <summary>
    /// Releases a COM object if it is one, ignoring failures (the RCW may already be
    /// detached).
    /// </summary>
    private static void ReleaseComObject(object? instance)
    {
        if (instance is null || !OperatingSystem.IsWindows() || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(instance);
        }
        catch (Exception)
        {
            // Nothing useful to do while tearing down an enumeration.
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int classContext, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

/// <summary>
/// <c>IMMDeviceEnumerator</c>: only <c>GetDefaultAudioEndpoint</c> is used, but the
/// preceding vtable slot must be declared for the call to dispatch correctly.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    /// <summary>Enumerates the audio endpoints of a data flow.</summary>
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

    /// <summary>Gets the default endpoint of a data flow and role.</summary>
    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

    /// <summary>Gets an endpoint by id.</summary>
    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    /// <summary>Registers an endpoint notification callback.</summary>
    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    /// <summary>Unregisters an endpoint notification callback.</summary>
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

/// <summary>
/// <c>IMMDeviceCollection</c>: declared only to complete the enumerator's vtable.
/// </summary>
[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    /// <summary>Gets the number of endpoints.</summary>
    [PreserveSig]
    int GetCount(out uint count);

    /// <summary>Gets an endpoint by index.</summary>
    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

/// <summary>
/// <c>IMMDevice</c>: only <c>Activate</c> is used, but the rest of the vtable is
/// declared to keep slot order explicit.
/// </summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    /// <summary>Activates a service on the endpoint.</summary>
    [PreserveSig]
    int Activate(ref Guid iid, int classContext, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    /// <summary>Opens the endpoint's property store.</summary>
    [PreserveSig]
    int OpenPropertyStore(int access, out IntPtr properties);

    /// <summary>Gets the endpoint id.</summary>
    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    /// <summary>Gets the endpoint state.</summary>
    [PreserveSig]
    int GetState(out int state);
}

/// <summary>
/// <c>IAudioSessionManager2</c>: only <c>GetSessionEnumerator</c> is used; the preceding
/// inherited methods are declared for correct vtable dispatch.
/// </summary>
[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    /// <summary>Gets the session control for a session.</summary>
    [PreserveSig]
    int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);

    /// <summary>Gets the volume control for a session.</summary>
    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);

    /// <summary>Gets the session enumerator.</summary>
    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

    /// <summary>Registers a session notification callback.</summary>
    [PreserveSig]
    int RegisterSessionNotification(IntPtr sessionNotification);

    /// <summary>Unregisters a session notification callback.</summary>
    [PreserveSig]
    int UnregisterSessionNotification(IntPtr sessionNotification);

    /// <summary>Registers a duck notification callback.</summary>
    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

    /// <summary>Unregisters a duck notification callback.</summary>
    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

/// <summary>
/// <c>IAudioSessionEnumerator</c>: lists the sessions of an audio device.
/// </summary>
[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    /// <summary>Gets the number of sessions.</summary>
    [PreserveSig]
    int GetCount(out int sessionCount);

    /// <summary>Gets a session by index as an <c>IUnknown</c> (query for the control interface).</summary>
    [PreserveSig]
    int GetSession(int sessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

/// <summary>
/// <c>IAudioSessionControl2</c>: exposes the session's process id. The inherited
/// <c>IAudioSessionControl</c> methods are declared first so the vtable slots line up.
/// </summary>
[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    /// <summary>Gets the session state.</summary>
    [PreserveSig]
    int GetState(out int state);

    /// <summary>Gets the session display name.</summary>
    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    /// <summary>Sets the session display name.</summary>
    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, IntPtr eventContext);

    /// <summary>Gets the session icon path.</summary>
    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    /// <summary>Sets the session icon path.</summary>
    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, IntPtr eventContext);

    /// <summary>Gets the grouping parameter.</summary>
    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    /// <summary>Sets the grouping parameter.</summary>
    [PreserveSig]
    int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);

    /// <summary>Registers a session notification callback.</summary>
    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr client);

    /// <summary>Unregisters a session notification callback.</summary>
    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr client);

    /// <summary>Gets the session identifier.</summary>
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);

    /// <summary>Gets the session instance identifier.</summary>
    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);

    /// <summary>Gets the process id that owns the session.</summary>
    [PreserveSig]
    int GetProcessId(out uint processId);

    /// <summary>Returns S_OK for the system-sounds session, S_FALSE otherwise.</summary>
    [PreserveSig]
    int IsSystemSoundsSession();

    /// <summary>Sets the ducking preference.</summary>
    [PreserveSig]
    int SetDuckingPreference(int optOut);
}