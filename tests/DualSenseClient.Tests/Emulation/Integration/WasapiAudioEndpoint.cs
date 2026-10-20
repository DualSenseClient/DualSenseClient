using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// Test-only WASAPI endpoint helper of the gated Emulation audio integration tests. It enumerates the
/// active render and capture endpoints of this machine, identifies the endpoints an attach created by
/// their new device id (never by friendly name, because a physical DualSense or DualShock 4 may be
/// connected), opens one endpoint with an exclusive exact format or the shared mix format, writes a
/// tone to a render endpoint, drains PCM from a capture endpoint, and polls for endpoint removal after
/// a detach. The COM interop is a minimal declaration of <c>IMMDeviceEnumerator</c>,
/// <c>IMMDevice</c>, <c>IPropertyStore</c>, <c>IAudioClient</c>, <c>IAudioRenderClient</c> and
/// <c>IAudioCaptureClient</c>: no package is referenced. Every COM object is created and released on
/// the calling test thread (the runtime initializes that thread's apartment as MTA on the first COM
/// call), every failure path releases what it created in a <see langword="finally"/>, and a stream
/// object owns its audio client until <see cref="IDisposable.Dispose"/> releases it.
/// </summary>
public static class WasapiAudioEndpoint
{
    /// <summary>How long the helpers poll for the endpoints an attach created (the task's 5 s budget).</summary>
    public static readonly TimeSpan EndpointAppearanceTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Delay between two endpoint enumeration polls.</summary>
    private const int PollIntervalMilliseconds = 50;

    /// <summary><c>AUDCLNT_E_DEVICE_INVALIDATED</c>: the endpoint was invalidated while it was opening.</summary>
    private const int AudioClientDeviceInvalidatedHResult = unchecked((int)0x88890004);

    /// <summary><c>AUDCLNT_E_RESOURCES_INVALIDATED</c>: the audio service retired the endpoint instance.</summary>
    private const int AudioClientResourcesInvalidatedHResult = unchecked((int)0x88890026);

    /// <summary><c>EDataFlow.eRender</c>.</summary>
    private const int DataFlowRender = 0;

    /// <summary><c>EDataFlow.eCapture</c>.</summary>
    private const int DataFlowCapture = 1;

    /// <summary><c>DEVICE_STATE_ACTIVE</c>: only endpoints that are currently plugged in and enabled.</summary>
    private const int DeviceStateActive = 0x1;

    /// <summary><c>AUDCLNT_SHAREMODE_SHARED</c>.</summary>
    private const int ShareModeShared = 0;

    /// <summary><c>AUDCLNT_SHAREMODE_EXCLUSIVE</c>.</summary>
    private const int ShareModeExclusive = 1;

    /// <summary>No stream flags: the helper polls the padding instead of using an event handle.</summary>
    private const uint StreamFlagsNone = 0;

    /// <summary><c>CLSCTX_INPROC_SERVER</c>, the context the audio services expect.</summary>
    private const int ClsCtxInProcServer = 0x1;

    /// <summary><c>STGM_READ</c>, the access mode of the endpoint property store.</summary>
    private const int StgmRead = 0;

    /// <summary><c>WAVE_FORMAT_PCM</c>.</summary>
    private const ushort WaveFormatPcm = 1;

    /// <summary><c>WAVE_FORMAT_IEEE_FLOAT</c>.</summary>
    private const ushort WaveFormatIeeeFloat = 3;

    /// <summary><c>WAVE_FORMAT_EXTENSIBLE</c>.</summary>
    private const ushort WaveFormatExtensible = 0xFFFE;

    /// <summary><c>VT_LPWSTR</c>, the variant type of <c>PKEY_Device_FriendlyName</c>.</summary>
    private const ushort VarTypeLpwStr = 31;

    /// <summary><c>IID_IAudioClient</c> ({1CB9AD4C-DBFA-4C32-B178-C2F568A703B2}).</summary>
    private static readonly Guid AudioClientIid = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    /// <summary><c>IID_IAudioRenderClient</c> ({F294ACFC-3146-4483-A7BF-ADDCA7C260E2}).</summary>
    private static readonly Guid RenderClientIid = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    /// <summary><c>IID_IAudioCaptureClient</c> ({C8ADBD64-E71E-48A0-A4DE-185C395CD317}).</summary>
    private static readonly Guid CaptureClientIid = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    /// <summary><c>IID_IMMDeviceEnumerator</c> ({A95664D2-9614-4F35-A746-DE8DB63617E6}).</summary>
    private static readonly Guid DeviceEnumeratorIid = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");

    /// <summary><c>CLSID_MMDeviceEnumerator</c> ({BCDE0395-E52F-467C-8E3D-C4579291692E}).</summary>
    private static readonly Guid DeviceEnumeratorClsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary><c>PKEY_Device_FriendlyName</c> format id ({A45C254E-DF1C-4EFD-8020-67D146A850E0}).</summary>
    private static readonly Guid FriendlyNameFormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    /// <summary><c>PKEY_Device_FriendlyName</c> property id.</summary>
    private const uint FriendlyNamePropertyId = 14;

    /// <summary>Reads every active render and capture endpoint of this machine with id and friendly name.</summary>
    /// <returns>The snapshot the caller holds as the attach baseline.</returns>
    /// <exception cref="PlatformNotSupportedException">The platform is not Windows.</exception>
    /// <exception cref="WasapiException">A COM call of the enumeration failed.</exception>
    public static WasapiEndpointSnapshot CaptureActiveEndpoints()
    {
        RequireWindows();
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            List<WasapiEndpointInfo> renderEndpoints = EnumerateEndpoints(enumerator, DataFlowRender);
            List<WasapiEndpointInfo> captureEndpoints = EnumerateEndpoints(enumerator, DataFlowCapture);
            return new WasapiEndpointSnapshot(renderEndpoints, captureEndpoints);
        }
        finally
        {
            ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Polls the active endpoints until at least one new endpoint exists in each flow or the timeout
    /// elapses, then returns the last observed diff. Endpoints are matched by their device id against
    /// <paramref name="baseline"/>, so an endpoint of a physically connected controller is never
    /// mistaken for the attached one.
    /// </summary>
    /// <param name="baseline">Active endpoints captured before the attach.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns>The endpoints that were not present in <paramref name="baseline"/> in the last poll.</returns>
    public static WasapiEndpointAppearance WaitForNewEndpoints(WasapiEndpointSnapshot baseline, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        Stopwatch stopwatch = Stopwatch.StartNew();
        WasapiEndpointAppearance appearance = new WasapiEndpointAppearance([], []);
        do
        {
            WasapiEndpointSnapshot current = CaptureActiveEndpoints();
            List<WasapiEndpointInfo> newRender = CollectNewEndpoints(current.RenderEndpoints, baseline);
            List<WasapiEndpointInfo> newCapture = CollectNewEndpoints(current.CaptureEndpoints, baseline);
            appearance = new WasapiEndpointAppearance(newRender, newCapture);
            if (newRender.Count > 0 && newCapture.Count > 0)
            {
                return appearance;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return appearance;
    }

    /// <summary>Polls the active endpoints until none of <paramref name="endpointIds"/> is active.</summary>
    /// <param name="endpointIds">Endpoint ids that must disappear (e.g. the ones the attach created).</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when every id disappeared; <see langword="false"/> on timeout.</returns>
    public static bool WaitForEndpointRemoval(IReadOnlyCollection<string> endpointIds, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(endpointIds);
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            WasapiEndpointSnapshot current = CaptureActiveEndpoints();
            bool anyPresent = false;
            foreach (string endpointId in endpointIds)
            {
                if (current.ContainsEndpoint(endpointId))
                {
                    anyPresent = true;
                    break;
                }
            }

            if (!anyPresent)
            {
                return true;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>
    /// Opens a render endpoint: first with an exclusive stream in the exact requested format, then
    /// with a shared stream in the endpoint's mix format. The negotiation outcome is carried by the
    /// returned stream so the test can disclose the format it really exercised.
    /// </summary>
    /// <param name="endpointId">Device id of the render endpoint.</param>
    /// <param name="exactFormat">Format the persona's speaker endpoint serves (e.g. 4 ch S16LE 48 kHz).</param>
    /// <param name="preferShared">
    /// When <see langword="true"/>, open the shared mix-format stream even if the exclusive exact
    /// format is available. The Windows default-playback path is shared, so the clock probes must
    /// exercise that path explicitly.
    /// </param>
    /// <returns>The initialized render stream.</returns>
    /// <exception cref="WasapiException">Neither the exact exclusive format nor the shared mix format could be opened.</exception>
    public static WasapiRenderEndpoint OpenRenderEndpoint(string endpointId, WasapiAudioFormat exactFormat, bool preferShared = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        RequireWindows();
        IAudioClient? client = null;
        try
        {
            client = CreateInitializedAudioClient(
                endpointId,
                exactFormat,
                preferShared,
                out WasapiShareMode shareMode,
                out WasapiAudioFormat negotiatedFormat,
                out string negotiationNote);
            Guid iid = RenderClientIid;
            int hr = client.GetService(ref iid, out object service);
            if (hr < 0 || service is not IAudioRenderClient renderClient)
            {
                ReleaseComObject(service);
                throw new WasapiException("IAudioClient.GetService(IAudioRenderClient) failed", hr);
            }

            hr = client.GetBufferSize(out uint bufferFrames);
            if (hr < 0)
            {
                ReleaseComObject(renderClient);
                throw new WasapiException("IAudioClient.GetBufferSize failed for the render endpoint", hr);
            }

            WasapiRenderEndpoint stream = new WasapiRenderEndpoint(
                client,
                renderClient,
                bufferFrames,
                shareMode,
                negotiatedFormat,
                negotiationNote);
            client = null;
            return stream;
        }
        finally
        {
            ReleaseComObject(client);
        }
    }

    /// <summary>
    /// Opens a capture endpoint: first with an exclusive stream in the exact requested format, then
    /// with a shared stream in the endpoint's mix format. The negotiation outcome is carried by the
    /// returned stream so the test can disclose the format it really exercised.
    /// </summary>
    /// <param name="endpointId">Device id of the capture endpoint.</param>
    /// <param name="exactFormat">Format the persona's microphone endpoint serves (e.g. 2 ch S16LE 48 kHz).</param>
    /// <returns>The initialized capture stream.</returns>
    /// <exception cref="WasapiException">Neither the exact exclusive format nor the shared mix format could be opened.</exception>
    public static WasapiCaptureEndpoint OpenCaptureEndpoint(string endpointId, WasapiAudioFormat exactFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        RequireWindows();
        IAudioClient? client = null;
        try
        {
            client = CreateInitializedAudioClient(
                endpointId,
                exactFormat,
                false,
                out WasapiShareMode shareMode,
                out WasapiAudioFormat negotiatedFormat,
                out string negotiationNote);
            Guid iid = CaptureClientIid;
            int hr = client.GetService(ref iid, out object service);
            if (hr < 0 || service is not IAudioCaptureClient captureClient)
            {
                ReleaseComObject(service);
                throw new WasapiException("IAudioClient.GetService(IAudioCaptureClient) failed", hr);
            }

            WasapiCaptureEndpoint stream = new WasapiCaptureEndpoint(
                client,
                captureClient,
                shareMode,
                negotiatedFormat,
                negotiationNote);
            client = null;
            return stream;
        }
        finally
        {
            ReleaseComObject(client);
        }
    }

    /// <summary>
    /// Opens the capture endpoint with one bounded retry when Windows invalidates the freshly reused
    /// MMDevice id while the audio service is still retiring the previous attach. The gated suite
    /// reuses the same ids across attach cycles, so <c>AUDCLNT_E_DEVICE_INVALIDATED</c> and
    /// <c>AUDCLNT_E_RESOURCES_INVALIDATED</c> during the open are transient; the retry is disclosed.
    /// </summary>
    /// <param name="endpointId">Device id of the capture endpoint.</param>
    /// <param name="exactFormat">Exact persona microphone format to try exclusively first.</param>
    /// <param name="attempts">Maximum attempts, including the first open.</param>
    /// <returns>The initialized capture stream.</returns>
    /// <exception cref="WasapiException">Every attempt failed, or the failure is not a transient invalidation.</exception>
    public static WasapiCaptureEndpoint OpenCaptureEndpointWithRetry(
        string endpointId,
        WasapiAudioFormat exactFormat,
        int attempts = 2)
    {
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "At least one attempt is required.");
        }

        for (int attempt = 1;; attempt++)
        {
            try
            {
                return OpenCaptureEndpoint(endpointId, exactFormat);
            }
            catch (WasapiException exception) when (
                attempt < attempts
                && exception.HResult is AudioClientDeviceInvalidatedHResult or AudioClientResourcesInvalidatedHResult)
            {
                TestContext.Out.WriteLine(
                    $"Capture endpoint open failed with 0x{exception.HResult:X8}; "
                    + $"retrying after the audio service settles (attempt {attempt + 1}/{attempts}).");
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Throws <see cref="PlatformNotSupportedException"/> when WASAPI is unavailable.</summary>
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI endpoint enumeration is only available on Windows.");
        }
    }

    /// <summary>Creates the MMDeviceEnumerator COM object and returns its enumerator interface.</summary>
    private static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        Guid clsid = DeviceEnumeratorClsid;
        Guid iid = DeviceEnumeratorIid;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtxInProcServer, ref iid, out object instance);
        if (hr < 0)
        {
            throw new WasapiException("CoCreateInstance(MMDeviceEnumerator) failed", hr);
        }

        if (instance is not IMMDeviceEnumerator enumerator)
        {
            ReleaseComObject(instance);
            throw new WasapiException("MMDeviceEnumerator did not expose IMMDeviceEnumerator");
        }

        return enumerator;
    }

    /// <summary>Enumerates the active endpoints of one data flow with their ids and friendly names.</summary>
    private static List<WasapiEndpointInfo> EnumerateEndpoints(IMMDeviceEnumerator enumerator, int dataFlow)
    {
        int hr = enumerator.EnumAudioEndpoints(dataFlow, DeviceStateActive, out IMMDeviceCollection? collection);
        if (hr < 0 || collection is null)
        {
            throw new WasapiException($"IMMDeviceEnumerator.EnumAudioEndpoints(flow {dataFlow}) failed", hr);
        }

        try
        {
            hr = collection.GetCount(out uint count);
            if (hr < 0)
            {
                throw new WasapiException("IMMDeviceCollection.GetCount failed", hr);
            }

            List<WasapiEndpointInfo> endpoints = [];
            for (uint index = 0; index < count; index++)
            {
                hr = collection.Item(index, out IMMDevice? device);
                if (hr < 0 || device is null)
                {
                    continue;
                }

                try
                {
                    hr = device.GetId(out string? id);
                    if (hr < 0 || string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    endpoints.Add(new WasapiEndpointInfo(id, ReadFriendlyName(device)));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return endpoints;
        }
        finally
        {
            ReleaseComObject(collection);
        }
    }

    /// <summary>Reads <c>PKEY_Device_FriendlyName</c> from an endpoint, or a placeholder when it is absent.</summary>
    private static string ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            int hr = device.OpenPropertyStore(StgmRead, out propertyStore);
            if (hr < 0 || propertyStore is null)
            {
                return "(no friendly name)";
            }

            PropertyKey key = new PropertyKey
            {
                FormatId = FriendlyNameFormatId,
                PropertyId = FriendlyNamePropertyId
            };
            hr = propertyStore.GetValue(ref key, out PropVariant value);
            if (hr < 0)
            {
                return "(no friendly name)";
            }

            try
            {
                if (value.ValueType != VarTypeLpwStr || value.PointerValue == IntPtr.Zero)
                {
                    return "(no friendly name)";
                }

                return Marshal.PtrToStringUni(value.PointerValue) ?? "(no friendly name)";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            ReleaseComObject(propertyStore);
        }
    }

    /// <summary>
    /// Creates an initialized audio client for <paramref name="endpointId"/>: exclusive with the exact
    /// format when the endpoint accepts it, shared with the mix format otherwise.
    /// </summary>
    private static IAudioClient CreateInitializedAudioClient(
        string endpointId,
        WasapiAudioFormat exactFormat,
        bool preferShared,
        out WasapiShareMode shareMode,
        out WasapiAudioFormat negotiatedFormat,
        out string negotiationNote)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        try
        {
            enumerator = CreateDeviceEnumerator();
            int hr = enumerator.GetDevice(endpointId, out device);
            if (hr < 0 || device is null)
            {
                throw new WasapiException($"IMMDeviceEnumerator.GetDevice(\"{endpointId}\") failed", hr);
            }

            if (preferShared)
            {
                client = TryCreateSharedClient(device, out WasapiAudioFormat preferredMixFormat, out string preferredSharedFailure);
                if (client is not null)
                {
                    shareMode = WasapiShareMode.Shared;
                    negotiatedFormat = preferredMixFormat;
                    negotiationNote = $"shared mix format {preferredMixFormat} (requested by the caller)";
                    IAudioClient preferredSharedResult = client;
                    client = null;
                    return preferredSharedResult;
                }

                client = TryCreateExclusiveClient(device, exactFormat, out string preferredExclusiveFailure);
                if (client is not null)
                {
                    shareMode = WasapiShareMode.Exclusive;
                    negotiatedFormat = exactFormat;
                    negotiationNote = $"exclusive exact format {exactFormat}; the shared mix format was unavailable ({preferredSharedFailure})";
                    IAudioClient preferredExclusiveResult = client;
                    client = null;
                    return preferredExclusiveResult;
                }

                throw new WasapiException(
                    $"Neither the shared mix format nor the exclusive exact format could be opened for endpoint \"{endpointId}\"; "
                    + $"shared: {preferredSharedFailure}; exclusive: {preferredExclusiveFailure}");
            }

            client = TryCreateExclusiveClient(device, exactFormat, out string exclusiveFailure);
            if (client is not null)
            {
                shareMode = WasapiShareMode.Exclusive;
                negotiatedFormat = exactFormat;
                negotiationNote = $"exclusive exact format {exactFormat}";
                IAudioClient exclusiveResult = client;
                client = null;
                return exclusiveResult;
            }

            client = TryCreateSharedClient(device, out WasapiAudioFormat mixFormat, out string sharedFailure);
            if (client is null)
            {
                throw new WasapiException(
                    $"Neither the exclusive exact format nor the shared mix format could be opened for endpoint \"{endpointId}\"; "
                    + $"exclusive: {exclusiveFailure}; shared: {sharedFailure}");
            }

            shareMode = WasapiShareMode.Shared;
            negotiatedFormat = mixFormat;
            negotiationNote = $"shared mix format {mixFormat}; the exclusive exact format was unavailable ({exclusiveFailure})";
            IAudioClient sharedResult = client;
            client = null;
            return sharedResult;
        }
        finally
        {
            ReleaseComObject(client);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Tries the exact format in exclusive mode. WAVEFORMATEXTENSIBLE is attempted first for more
    /// than two channels and plain WAVEFORMATEX first for mono/stereo; the other representation is
    /// tried when the first one is not accepted. Returns <see langword="null"/> and a diagnostic when
    /// no representation initializes.
    /// </summary>
    private static IAudioClient? TryCreateExclusiveClient(IMMDevice device, WasapiAudioFormat exactFormat, out string failure)
    {
        List<string> failures = [];
        foreach (byte[] formatBlob in CreateWaveFormatBlobs(exactFormat))
        {
            IntPtr formatPointer = AllocateFormat(formatBlob);
            IAudioClient? client = null;
            try
            {
                client = ActivateAudioClient(device);
                int hr = client.IsFormatSupported(ShareModeExclusive, formatPointer, out IntPtr closestMatch);
                if (closestMatch != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(closestMatch);
                }

                if (hr != 0)
                {
                    failures.Add($"IsFormatSupported returned 0x{hr:X8}");
                    ReleaseComObject(client);
                    client = null;
                    continue;
                }

                hr = client.GetDevicePeriod(out long defaultPeriod, out _);
                long period = hr >= 0 && defaultPeriod > 0 ? defaultPeriod : 0;
                hr = client.Initialize(ShareModeExclusive, StreamFlagsNone, period, period, formatPointer, IntPtr.Zero);
                if (hr < 0)
                {
                    failures.Add($"Initialize returned 0x{hr:X8}");
                    ReleaseComObject(client);
                    client = null;
                    continue;
                }

                failure = string.Empty;
                IAudioClient result = client;
                client = null;
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(formatPointer);
                ReleaseComObject(client);
            }
        }

        failure = failures.Count > 0 ? string.Join("; ", failures) : "no format representation was attempted";
        return null;
    }

    /// <summary>
    /// Opens a shared-mode client with the endpoint's mix format. Returns <see langword="null"/> and a
    /// diagnostic when the format cannot be read or the shared stream cannot be initialized.
    /// </summary>
    private static IAudioClient? TryCreateSharedClient(
        IMMDevice device,
        out WasapiAudioFormat mixFormat,
        out string failure)
    {
        mixFormat = default;
        IAudioClient? client = null;
        IntPtr mixFormatPointer = IntPtr.Zero;
        try
        {
            client = ActivateAudioClient(device);
            int hr = client.GetMixFormat(out mixFormatPointer);
            if (hr < 0 || mixFormatPointer == IntPtr.Zero)
            {
                failure = $"IAudioClient.GetMixFormat returned 0x{hr:X8}";
                return null;
            }

            WasapiAudioFormat parsedFormat;
            try
            {
                parsedFormat = ParseWaveFormat(mixFormatPointer);
            }
            catch (Exception exception) when (exception is WasapiException or ArgumentOutOfRangeException)
            {
                failure = $"the shared mix format could not be used: {exception.Message}";
                return null;
            }

            hr = client.Initialize(ShareModeShared, StreamFlagsNone, 0, 0, mixFormatPointer, IntPtr.Zero);
            if (hr < 0)
            {
                failure = $"IAudioClient.Initialize(shared) returned 0x{hr:X8}";
                return null;
            }

            mixFormat = parsedFormat;
            failure = string.Empty;
            IAudioClient result = client;
            client = null;
            return result;
        }
        finally
        {
            if (mixFormatPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormatPointer);
            }

            ReleaseComObject(client);
        }
    }

    /// <summary>Activates the <c>IAudioClient</c> service on an endpoint device.</summary>
    private static IAudioClient ActivateAudioClient(IMMDevice device)
    {
        Guid iid = AudioClientIid;
        int hr = device.Activate(ref iid, ClsCtxInProcServer, IntPtr.Zero, out object instance);
        if (hr < 0)
        {
            throw new WasapiException("IMMDevice.Activate(IAudioClient) failed", hr);
        }

        if (instance is not IAudioClient client)
        {
            ReleaseComObject(instance);
            throw new WasapiException("The activated audio service did not expose IAudioClient");
        }

        return client;
    }

    /// <summary>Builds the WAVEFORMATEX/WAVEFORMATEXTENSIBLE representations tried in exclusive mode.</summary>
    private static List<byte[]> CreateWaveFormatBlobs(WasapiAudioFormat format)
    {
        List<byte[]> blobs = [];
        bool extensibleFirst = format.Channels > 2;
        blobs.Add(CreateWaveFormatBlob(format, extensibleFirst));
        blobs.Add(CreateWaveFormatBlob(format, !extensibleFirst));
        return blobs;
    }

    /// <summary>Builds one WAVEFORMATEX (18 bytes) or WAVEFORMATEXTENSIBLE (40 bytes) blob.</summary>
    private static byte[] CreateWaveFormatBlob(WasapiAudioFormat format, bool extensible)
    {
        ValidateFormat(format);
        int size = extensible ? 40 : 18;
        byte[] blob = new byte[size];
        ushort formatTag = format.IsFloat ? WaveFormatIeeeFloat : WaveFormatPcm;
        if (extensible)
        {
            formatTag = WaveFormatExtensible;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(0), formatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2), (ushort)format.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4), (uint)format.SampleRateHz);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(8), (uint)(format.SampleRateHz * format.BlockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12), (ushort)format.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(14), (ushort)format.BitsPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(16), extensible ? (ushort)22 : (ushort)0);
        if (!extensible)
        {
            return blob;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(18), (ushort)format.BitsPerSample);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(20), GetChannelMask(format.Channels));
        ReadOnlySpan<byte> subFormat = format.IsFloat ? IeeeFloatSubFormat() : PcmSubFormat();
        subFormat.CopyTo(blob.AsSpan(24, 16));
        return blob;
    }

    /// <summary>Returns the KSDATAFORMAT_SUBTYPE_PCM GUID bytes.</summary>
    private static ReadOnlySpan<byte> PcmSubFormat() => [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    /// <summary>Returns the KSDATAFORMAT_SUBTYPE_IEEE_FLOAT GUID bytes.</summary>
    private static ReadOnlySpan<byte> IeeeFloatSubFormat() => [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    /// <summary>Returns the speaker mask that matches the persona's channel layout.</summary>
    private static uint GetChannelMask(int channels)
    {
        return channels switch
        {
            1 => 0x4,
            2 => 0x3,
            4 => 0x33,
            6 => 0x3F,
            _ => 0
        };
    }

    /// <summary>Validates a format the helper can write, read and describe.</summary>
    private static void ValidateFormat(WasapiAudioFormat format)
    {
        if (format.Channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.Channels, "The format must have at least one channel.");
        }

        if (format.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.SampleRateHz, "The format must have a positive sample rate.");
        }

        if (format.BitsPerSample != 16 && format.BitsPerSample != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.BitsPerSample, "The format must use 16-bit or 32-bit samples.");
        }

        if (format.IsFloat && format.BitsPerSample != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.BitsPerSample, "A floating-point format must use 32-bit samples.");
        }
    }

    /// <summary>Allocates an unmanaged copy of a WAVEFORMATEX/WAVEFORMATEXTENSIBLE blob.</summary>
    private static IntPtr AllocateFormat(byte[] blob)
    {
        IntPtr pointer = Marshal.AllocHGlobal(blob.Length);
        Marshal.Copy(blob, 0, pointer, blob.Length);
        return pointer;
    }

    /// <summary>Parses a native WAVEFORMATEX/WAVEFORMATEXTENSIBLE pointer into the helper's format record.</summary>
    private static WasapiAudioFormat ParseWaveFormat(IntPtr formatPointer)
    {
        ushort formatTag = (ushort)Marshal.ReadInt16(formatPointer, 0);
        ushort channels = (ushort)Marshal.ReadInt16(formatPointer, 2);
        uint sampleRate = (uint)Marshal.ReadInt32(formatPointer, 4);
        ushort bitsPerSample = (ushort)Marshal.ReadInt16(formatPointer, 14);
        bool isFloat;
        if (formatTag == WaveFormatExtensible)
        {
            uint subFormat = (uint)Marshal.ReadInt32(formatPointer, 24);
            if (subFormat == WaveFormatPcm)
            {
                isFloat = false;
            }
            else if (subFormat == WaveFormatIeeeFloat)
            {
                isFloat = true;
            }
            else
            {
                throw new WasapiException($"The shared mix format uses the unsupported sub-format {subFormat}.");
            }
        }
        else if (formatTag == WaveFormatPcm)
        {
            isFloat = false;
        }
        else if (formatTag == WaveFormatIeeeFloat)
        {
            isFloat = true;
        }
        else
        {
            throw new WasapiException($"The shared mix format uses the unsupported format tag 0x{formatTag:X4}.");
        }

        return new WasapiAudioFormat(channels, (int)sampleRate, bitsPerSample, isFloat);
    }

    /// <summary>Collects the endpoints of <paramref name="current"/> whose id is absent from the baseline.</summary>
    private static List<WasapiEndpointInfo> CollectNewEndpoints(
        IReadOnlyList<WasapiEndpointInfo> current,
        WasapiEndpointSnapshot baseline)
    {
        List<WasapiEndpointInfo> newEndpoints = [];
        foreach (WasapiEndpointInfo endpoint in current)
        {
            if (!baseline.ContainsEndpoint(endpoint.Id))
            {
                newEndpoints.Add(endpoint);
            }
        }

        return newEndpoints;
    }

    /// <summary>Releases a COM object if it is one, ignoring failures (the RCW may already be detached).</summary>
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
            // Nothing useful to do while tearing down one helper call.
        }
    }

    /// <summary>Creates the MMDeviceEnumerator COM object.</summary>
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        int classContext,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    /// <summary>Clears a PROPVARIANT returned by <c>IPropertyStore.GetValue</c>.</summary>
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);
}

/// <summary><c>IMMDeviceEnumerator</c>: enumerates endpoints and opens one by id; the full vtable is declared.</summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    /// <summary>Enumerates the endpoints of a data flow.</summary>
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);

    /// <summary>Gets the default endpoint of a data flow and role.</summary>
    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);

    /// <summary>Gets an endpoint by device id.</summary>
    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);

    /// <summary>Registers an endpoint notification callback.</summary>
    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    /// <summary>Unregisters an endpoint notification callback.</summary>
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

/// <summary><c>IMMDeviceCollection</c>: lists the endpoints of one data flow.</summary>
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
    int Item(uint index, out IMMDevice? device);
}

/// <summary><c>IMMDevice</c>: activates a service, opens the property store and returns id and state.</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    /// <summary>Activates a service on the endpoint.</summary>
    [PreserveSig]
    int Activate(
        ref Guid iid,
        int classContext,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    /// <summary>Opens the endpoint's property store.</summary>
    [PreserveSig]
    int OpenPropertyStore(int access, out IPropertyStore? properties);

    /// <summary>Gets the endpoint id.</summary>
    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);

    /// <summary>Gets the endpoint state.</summary>
    [PreserveSig]
    int GetState(out int state);
}

/// <summary><c>IPropertyStore</c>: reads one endpoint property.</summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    /// <summary>Gets the number of properties.</summary>
    [PreserveSig]
    int GetCount(out uint propertyCount);

    /// <summary>Gets a property key by index.</summary>
    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    /// <summary>Gets a property value.</summary>
    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    /// <summary>Sets a property value.</summary>
    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    /// <summary>Commits pending writes.</summary>
    [PreserveSig]
    int Commit();
}

/// <summary><c>IAudioClient</c>: the audio stream surface the helper initializes and drives.</summary>
[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    /// <summary>Initializes the audio stream.</summary>
    [PreserveSig]
    int Initialize(
        int shareMode,
        uint streamFlags,
        long bufferDuration,
        long periodicity,
        IntPtr format,
        IntPtr audioSessionGuid);

    /// <summary>Gets the buffer size in frames.</summary>
    [PreserveSig]
    int GetBufferSize(out uint bufferFrames);

    /// <summary>Gets the stream latency.</summary>
    [PreserveSig]
    int GetStreamLatency(out long latency);

    /// <summary>Gets the number of frames in the buffer.</summary>
    [PreserveSig]
    int GetCurrentPadding(out uint padding);

    /// <summary>Tests a format for support.</summary>
    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

    /// <summary>Gets the device mix format; the caller frees the returned pointer with CoTaskMemFree.</summary>
    [PreserveSig]
    int GetMixFormat(out IntPtr format);

    /// <summary>Gets the device period.</summary>
    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    /// <summary>Starts the stream.</summary>
    [PreserveSig]
    int Start();

    /// <summary>Stops the stream.</summary>
    [PreserveSig]
    int Stop();

    /// <summary>Resets the stream.</summary>
    [PreserveSig]
    int Reset();

    /// <summary>Sets the event the engine signals when a buffer is ready.</summary>
    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    /// <summary>Retrieves a service from the audio client.</summary>
    [PreserveSig]
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

/// <summary><c>IAudioRenderClient</c>: fills the shared or exclusive render buffer.</summary>
[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    /// <summary>Gets a pointer to the next writable buffer slice.</summary>
    [PreserveSig]
    int GetBuffer(uint numFramesRequested, out IntPtr data);

    /// <summary>Releases a buffer slice after writing it.</summary>
    [PreserveSig]
    int ReleaseBuffer(uint numFramesWritten, uint flags);
}

/// <summary><c>IAudioCaptureClient</c>: drains captured packets.</summary>
[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    /// <summary>Gets a pointer to the next captured packet.</summary>
    [PreserveSig]
    int GetBuffer(
        out IntPtr data,
        out uint numFramesToRead,
        out uint flags,
        out ulong devicePosition,
        out ulong qpcPosition);

    /// <summary>Releases a packet previously obtained with <see cref="GetBuffer"/>.</summary>
    [PreserveSig]
    int ReleaseBuffer(uint numFramesRead);

    /// <summary>Gets the size of the next available packet in frames.</summary>
    [PreserveSig]
    int GetNextPacketSize(out uint numFramesInNextPacket);
}

/// <summary><c>PROPERTYKEY</c>: format id plus property id.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    /// <summary>Property format id.</summary>
    public Guid FormatId;

    /// <summary>Property id within the format.</summary>
    public uint PropertyId;
}

/// <summary>
/// <c>PROPVARIANT</c> with only the members the friendly-name read needs; sized like the native
/// structure so the API can write the whole union without overrunning the managed buffer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    /// <summary>Variant type (<c>vt</c>).</summary>
    [FieldOffset(0)] public ushort ValueType;

    /// <summary>Pointer member of the union; a UTF-16 string for <c>VT_LPWSTR</c>.</summary>
    [FieldOffset(8)] public IntPtr PointerValue;

    /// <summary>Tail of the union, never read by the helper; keeps the managed size correct.</summary>
    [FieldOffset(16)] public IntPtr Padding;
}

/// <summary>Share mode of an opened WASAPI stream, disclosed by the audio endpoint tests.</summary>
public enum WasapiShareMode
{
    /// <summary>The audio engine mixes other clients into the stream.</summary>
    Shared = 0,

    /// <summary>The test owns the endpoint exclusively for the stream's lifetime.</summary>
    Exclusive = 1
}

/// <summary>PCM geometry of one WASAPI endpoint as negotiated for a test stream.</summary>
public readonly record struct WasapiAudioFormat
{
    /// <summary>Creates a format record and validates the geometry the helper can write and read.</summary>
    /// <param name="channels">Number of interleaved channels.</param>
    /// <param name="sampleRateHz">Sample rate in hertz.</param>
    /// <param name="bitsPerSample">Bits per sample: 16 or 32.</param>
    /// <param name="isFloat">Whether samples are IEEE-754 floating point (only valid for 32 bits).</param>
    public WasapiAudioFormat(int channels, int sampleRateHz, int bitsPerSample, bool isFloat)
    {
        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "A format must have at least one channel.");
        }

        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), sampleRateHz, "A format must have a positive sample rate.");
        }

        if (bitsPerSample != 16 && bitsPerSample != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, "A format must use 16-bit or 32-bit samples.");
        }

        if (isFloat && bitsPerSample != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(isFloat), isFloat, "A floating-point format must use 32-bit samples.");
        }

        Channels = channels;
        SampleRateHz = sampleRateHz;
        BitsPerSample = bitsPerSample;
        IsFloat = isFloat;
    }

    /// <summary>Number of interleaved channels.</summary>
    public int Channels { get; }

    /// <summary>Sample rate in hertz.</summary>
    public int SampleRateHz { get; }

    /// <summary>Bits per sample.</summary>
    public int BitsPerSample { get; }

    /// <summary>Whether samples are IEEE-754 floating point.</summary>
    public bool IsFloat { get; }

    /// <summary>Bytes of one interleaved sample frame.</summary>
    public int BlockAlign
    {
        get
        {
            return Channels * BitsPerSample / 8;
        }
    }

    /// <summary>Describes the format for test output and failure diagnostics.</summary>
    /// <returns>One line such as "4 ch PCM 16-bit 48000 Hz".</returns>
    public override string ToString() => $"{Channels} ch {(IsFloat ? "float" : "PCM")} {BitsPerSample}-bit {SampleRateHz} Hz";
}

/// <summary>One active WASAPI endpoint as captured before or after an attach.</summary>
/// <param name="Id">MMDevice device id of the endpoint; the identity the tests diff.</param>
/// <param name="FriendlyName">Display name of the endpoint, recorded for the report only.</param>
public sealed record WasapiEndpointInfo(string Id, string FriendlyName);

/// <summary>Active render and capture endpoints observed at one point in time.</summary>
public sealed class WasapiEndpointSnapshot
{
    private readonly HashSet<string> _endpointIds;

    /// <summary>Creates a snapshot from the enumerated render and capture endpoint lists.</summary>
    /// <param name="renderEndpoints">Active render endpoints.</param>
    /// <param name="captureEndpoints">Active capture endpoints.</param>
    public WasapiEndpointSnapshot(
        IReadOnlyList<WasapiEndpointInfo> renderEndpoints,
        IReadOnlyList<WasapiEndpointInfo> captureEndpoints)
    {
        ArgumentNullException.ThrowIfNull(renderEndpoints);
        ArgumentNullException.ThrowIfNull(captureEndpoints);
        RenderEndpoints = renderEndpoints;
        CaptureEndpoints = captureEndpoints;
        _endpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WasapiEndpointInfo endpoint in renderEndpoints)
        {
            _endpointIds.Add(endpoint.Id);
        }

        foreach (WasapiEndpointInfo endpoint in captureEndpoints)
        {
            _endpointIds.Add(endpoint.Id);
        }
    }

    /// <summary>Active render endpoints, in enumeration order.</summary>
    public IReadOnlyList<WasapiEndpointInfo> RenderEndpoints { get; }

    /// <summary>Active capture endpoints, in enumeration order.</summary>
    public IReadOnlyList<WasapiEndpointInfo> CaptureEndpoints { get; }

    /// <summary>Returns whether an endpoint id of either flow is part of this snapshot.</summary>
    /// <param name="endpointId">MMDevice device id to look up.</param>
    /// <returns><see langword="true"/> when the id is active in either flow.</returns>
    public bool ContainsEndpoint(string endpointId) => _endpointIds.Contains(endpointId);
}

/// <summary>The endpoints that appeared since a baseline snapshot, one list per data flow.</summary>
/// <param name="NewRenderEndpoints">Render endpoints absent from the baseline.</param>
/// <param name="NewCaptureEndpoints">Capture endpoints absent from the baseline.</param>
public sealed record WasapiEndpointAppearance(
    IReadOnlyList<WasapiEndpointInfo> NewRenderEndpoints,
    IReadOnlyList<WasapiEndpointInfo> NewCaptureEndpoints);

/// <summary>
/// One opened WASAPI render endpoint. <see cref="PlayTone"/> writes a continuous tone in the
/// negotiated format; the stream keeps playing the buffered tail until <see cref="Dispose"/> stops it
/// and releases the audio client.
/// </summary>
public sealed class WasapiRenderEndpoint : IDisposable
{
    /// <summary>Delay after the last written frame that lets the buffered tail reach the device.</summary>
    private static readonly TimeSpan TailDrainDelay = TimeSpan.FromMilliseconds(50);

    private IAudioClient? _client;
    private IAudioRenderClient? _renderClient;
    private readonly int _bufferFrames;
    private readonly int _frameBytes;
    private bool _started;
    private bool _disposed;

    /// <summary>Stores the initialized client and the negotiation outcome; called by the factory only.</summary>
    internal WasapiRenderEndpoint(
        IAudioClient client,
        IAudioRenderClient renderClient,
        uint bufferFrames,
        WasapiShareMode shareMode,
        WasapiAudioFormat format,
        string negotiationNote)
    {
        _client = client;
        _renderClient = renderClient;
        _bufferFrames = checked((int)bufferFrames);
        _frameBytes = format.BlockAlign;
        ShareMode = shareMode;
        Format = format;
        NegotiationNote = negotiationNote;
    }

    /// <summary>Whether the stream runs exclusively or through the shared audio engine.</summary>
    public WasapiShareMode ShareMode { get; }

    /// <summary>Format the stream was initialized with (the exact format or the shared mix format).</summary>
    public WasapiAudioFormat Format { get; }

    /// <summary>Human-readable negotiation detail for the report and failure diagnostics.</summary>
    public string NegotiationNote { get; }

    /// <summary>Size of the render buffer in frames.</summary>
    public int BufferFrames
    {
        get
        {
            return _bufferFrames;
        }
    }

    /// <summary>
    /// Plays a sine tone on every negotiated channel for <paramref name="duration"/>. The buffer is
    /// prefilled before the stream starts, then kept filled until the requested frame count has been
    /// written; a short bounded wait lets the tail drain before the caller disposes the stream.
    /// </summary>
    /// <param name="frequencyHz">Tone frequency in hertz.</param>
    /// <param name="duration">How much audio to write.</param>
    /// <param name="amplitude">Peak amplitude in the range (0, 1].</param>
    public void PlayTone(double frequencyHz, TimeSpan duration, double amplitude)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frequencyHz <= 0 || double.IsNaN(frequencyHz) || double.IsInfinity(frequencyHz))
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), frequencyHz, "The tone frequency must be finite and positive.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The tone duration must be positive.");
        }

        if (amplitude <= 0 || amplitude > 1 || double.IsNaN(amplitude))
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), amplitude, "The tone amplitude must be in the range (0, 1].");
        }

        if (_started)
        {
            throw new InvalidOperationException("The render stream has already been started.");
        }

        IAudioClient client = _client ?? throw new ObjectDisposedException(nameof(WasapiRenderEndpoint));
        int totalFrames = checked((int)Math.Ceiling(duration.TotalSeconds * Format.SampleRateHz));
        double phase = 0;
        int framesWritten = WriteAvailable(client, frequencyHz, amplitude, ref phase, totalFrames);
        int hr = client.Start();
        if (hr < 0)
        {
            throw new WasapiException("IAudioClient.Start failed for the render endpoint", hr);
        }

        _started = true;
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan limit = duration + TimeSpan.FromSeconds(2);
        while (framesWritten < totalFrames && stopwatch.Elapsed < limit)
        {
            int written = WriteAvailable(client, frequencyHz, amplitude, ref phase, totalFrames - framesWritten);
            framesWritten += written;
            if (written == 0)
            {
                Thread.Sleep(1);
            }
        }

        if (framesWritten < totalFrames)
        {
            throw new InvalidOperationException(
                $"The render endpoint accepted only {framesWritten} of {totalFrames} tone frames within "
                + $"{limit.TotalSeconds:0.#} s.");
        }

        Thread.Sleep(TailDrainDelay);
    }

    /// <summary>Stops the stream and releases the render and audio client interfaces.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started && _client is not null)
        {
            _client.Stop();
            _started = false;
        }

        ReleaseClient(_renderClient);
        _renderClient = null;
        ReleaseClient(_client);
        _client = null;
    }

    /// <summary>Writes as many tone frames as the buffer has free space for, up to <paramref name="maximumFrames"/>.</summary>
    private int WriteAvailable(
        IAudioClient client,
        double frequencyHz,
        double amplitude,
        ref double phase,
        int maximumFrames)
    {
        if (maximumFrames <= 0)
        {
            return 0;
        }

        int hr = client.GetCurrentPadding(out uint padding);
        if (hr < 0)
        {
            throw new WasapiException("IAudioClient.GetCurrentPadding failed for the render endpoint", hr);
        }

        int available = _bufferFrames - (int)padding;
        if (available <= 0)
        {
            return 0;
        }

        int frames = Math.Min(available, maximumFrames);
        IAudioRenderClient renderClient = _renderClient ?? throw new ObjectDisposedException(nameof(WasapiRenderEndpoint));
        hr = renderClient.GetBuffer((uint)frames, out IntPtr data);
        if (hr < 0)
        {
            throw new WasapiException("IAudioRenderClient.GetBuffer failed", hr);
        }

        try
        {
            byte[] block = CreateToneBlock(frequencyHz, amplitude, ref phase, frames);
            Marshal.Copy(block, 0, data, block.Length);
        }
        finally
        {
            hr = renderClient.ReleaseBuffer((uint)frames, 0);
            if (hr < 0)
            {
                throw new WasapiException("IAudioRenderClient.ReleaseBuffer failed", hr);
            }
        }

        return frames;
    }

    /// <summary>Builds one buffer slice of the tone in the negotiated sample format.</summary>
    private byte[] CreateToneBlock(double frequencyHz, double amplitude, ref double phase, int frames)
    {
        byte[] block = new byte[frames * _frameBytes];
        int bytesPerSample = Format.BitsPerSample / 8;
        double phaseIncrement = 2.0 * Math.PI * frequencyHz / Format.SampleRateHz;
        int offset = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            double value = Math.Sin(phase) * amplitude;
            for (int channel = 0; channel < Format.Channels; channel++)
            {
                if (Format.IsFloat)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(offset, 4), (float)value);
                }
                else if (Format.BitsPerSample == 16)
                {
                    short sample = (short)Math.Clamp((int)Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(offset, 2), sample);
                }
                else
                {
                    int sample = (int)Math.Round(value * int.MaxValue);
                    BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(offset, 4), sample);
                }

                offset += bytesPerSample;
            }

            phase += phaseIncrement;
            if (phase >= 2.0 * Math.PI)
            {
                phase -= 2.0 * Math.PI;
            }
        }

        return block;
    }

    /// <summary>Releases one COM interface without letting a teardown failure escape Dispose.</summary>
    private static void ReleaseClient(object? instance)
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
            // Dispose must stay best effort; the process is about to release the endpoint anyway.
        }
    }
}

/// <summary>
/// One opened WASAPI capture endpoint. <see cref="DrainAvailable"/> copies every currently available
/// packet into a managed buffer; the stream is stopped and the audio client released by
/// <see cref="Dispose"/>.
/// </summary>
public sealed class WasapiCaptureEndpoint : IDisposable
{
    /// <summary><c>AUDCLNT_BUFFERFLAGS_SILENT</c>: the packet carries no valid data and reads as zeros.</summary>
    private const uint BufferFlagSilent = 0x2;

    private IAudioClient? _client;
    private IAudioCaptureClient? _captureClient;
    private readonly int _blockAlign;
    private bool _started;
    private bool _disposed;

    /// <summary>Stores the initialized client and the negotiation outcome; called by the factory only.</summary>
    internal WasapiCaptureEndpoint(
        IAudioClient client,
        IAudioCaptureClient captureClient,
        WasapiShareMode shareMode,
        WasapiAudioFormat format,
        string negotiationNote)
    {
        _client = client;
        _captureClient = captureClient;
        _blockAlign = format.BlockAlign;
        ShareMode = shareMode;
        Format = format;
        NegotiationNote = negotiationNote;
    }

    /// <summary>Whether the stream runs exclusively or through the shared audio engine.</summary>
    public WasapiShareMode ShareMode { get; }

    /// <summary>Format the stream was initialized with (the exact format or the shared mix format).</summary>
    public WasapiAudioFormat Format { get; }

    /// <summary>Human-readable negotiation detail for the report and failure diagnostics.</summary>
    public string NegotiationNote { get; }

    /// <summary>Starts the capture stream.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The capture stream has already been started.");
        }

        IAudioClient client = _client ?? throw new ObjectDisposedException(nameof(WasapiCaptureEndpoint));
        int hr = client.Start();
        if (hr < 0)
        {
            throw new WasapiException("IAudioClient.Start failed for the capture endpoint", hr);
        }

        _started = true;
    }

    /// <summary>Stops the capture stream; a stream that was never started is a no-op.</summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started || _client is null)
        {
            return;
        }

        _started = false;
        int hr = _client.Stop();
        if (hr < 0)
        {
            throw new WasapiException("IAudioClient.Stop failed for the capture endpoint", hr);
        }
    }

    /// <summary>
    /// Copies every packet the capture client currently holds into one managed byte array, in the
    /// negotiated format and including silent packets as zeros. An empty result means no packet was
    /// available at this moment.
    /// </summary>
    /// <returns>The captured bytes in stream order.</returns>
    public byte[] DrainAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IAudioCaptureClient captureClient = _captureClient ?? throw new ObjectDisposedException(nameof(WasapiCaptureEndpoint));
        using MemoryStream captured = new MemoryStream();
        while (true)
        {
            int hr = captureClient.GetNextPacketSize(out uint packetFrames);
            if (hr < 0)
            {
                throw new WasapiException("IAudioCaptureClient.GetNextPacketSize failed", hr);
            }

            if (packetFrames == 0)
            {
                break;
            }

            hr = captureClient.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
            if (hr < 0)
            {
                throw new WasapiException("IAudioCaptureClient.GetBuffer failed", hr);
            }

            try
            {
                int byteCount = checked((int)frames * _blockAlign);
                if ((flags & BufferFlagSilent) == 0 && data != IntPtr.Zero)
                {
                    byte[] packet = new byte[byteCount];
                    Marshal.Copy(data, packet, 0, byteCount);
                    captured.Write(packet, 0, packet.Length);
                }
                else
                {
                    captured.Write(new byte[byteCount], 0, byteCount);
                }
            }
            finally
            {
                hr = captureClient.ReleaseBuffer(frames);
                if (hr < 0)
                {
                    throw new WasapiException("IAudioCaptureClient.ReleaseBuffer failed", hr);
                }
            }
        }

        return captured.ToArray();
    }

    /// <summary>Stops the stream and releases the capture and audio client interfaces.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started && _client is not null)
        {
            _client.Stop();
            _started = false;
        }

        ReleaseClient(_captureClient);
        _captureClient = null;
        ReleaseClient(_client);
        _client = null;
    }

    /// <summary>Releases one COM interface without letting a teardown failure escape Dispose.</summary>
    private static void ReleaseClient(object? instance)
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
            // Dispose must stay best effort; the process is about to release the endpoint anyway.
        }
    }
}

/// <summary>Failure of a WASAPI COM call or of a stream negotiation, carrying the HRESULT when there is one.</summary>
public sealed class WasapiException : Exception
{
    /// <summary>Creates an exception with a message only.</summary>
    /// <param name="message">Failure description.</param>
    public WasapiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the failing HRESULT.</summary>
    /// <param name="message">Failure description.</param>
    /// <param name="hResult">HRESULT the COM call returned.</param>
    public WasapiException(string message, int hResult)
        : base($"{message} (HRESULT 0x{hResult:X8})")
    {
        HResult = hResult;
    }
}