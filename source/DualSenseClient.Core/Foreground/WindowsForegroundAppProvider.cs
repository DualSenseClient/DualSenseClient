using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseClient.Core.Foreground;

/// <summary>
/// Windows <see cref="IForegroundAppProvider"/> based on <c>GetForegroundWindow</c>.
/// Ported from DS4Windows' <c>AutoProfileChecker</c>: the foreground window handle is
/// cached so repeated polls while focus is unchanged skip the process lookup, and the
/// executable path falls back to the process name when the image path is unreadable.
/// </summary>
public sealed class WindowsForegroundAppProvider : IForegroundAppProvider
{
    /// <summary>
    /// Desired access for opening the foreground process to query its image path.
    /// </summary>
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Maximum executable path / window title length queried in one call.
    /// </summary>
    private const int BufferCapacity = 1000;

    /// <summary>
    /// Reused window title buffer so polls do not reallocate every second.
    /// </summary>
    private readonly StringBuilder _textBuilder = new StringBuilder(BufferCapacity);

    /// <summary>
    /// The foreground window of the previous poll, or zero when unknown.
    /// </summary>
    private IntPtr _prevWindow;

    /// <summary>
    /// The process id of the previous foreground window.
    /// </summary>
    private uint _prevProcessId;

    /// <summary>
    /// The cached executable path of the previous foreground window.
    /// </summary>
    private string _prevExePath = string.Empty;

    /// <inheritdoc/>
    public bool IsSupported
    {
        get
        {
            return OperatingSystem.IsWindows();
        }
    }

    /// <inheritdoc/>
    public ForegroundApp? GetForegroundApp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            _prevWindow = IntPtr.Zero;
            _prevProcessId = 0;
            _prevExePath = string.Empty;
            return null;
        }

        if (window == _prevWindow)
        {
            return new ForegroundApp(_prevExePath, GetWindowTitle(window));
        }

        _prevWindow = window;

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == _prevProcessId)
        {
            return new ForegroundApp(_prevExePath, GetWindowTitle(window));
        }

        _prevProcessId = processId;
        _prevExePath = GetProcessExecutablePath(processId).Replace('/', '\\');
        return new ForegroundApp(_prevExePath, GetWindowTitle(window));
    }

    /// <summary>
    /// Reads the current title of a window.
    /// </summary>
    private string GetWindowTitle(IntPtr window)
    {
        GetWindowText(window, _textBuilder, _textBuilder.Capacity);
        return _textBuilder.ToString();
    }

    /// <summary>
    /// Resolves the full executable path of a process, falling back to
    /// <c>ProcessName.exe</c> when the image path cannot be queried.
    /// </summary>
    private static string GetProcessExecutablePath(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process != IntPtr.Zero)
        {
            try
            {
                StringBuilder builder = new StringBuilder(BufferCapacity);
                int size = builder.Capacity;
                if (QueryFullProcessImageName(process, 0, builder, ref size) && size > 0)
                {
                    return builder.ToString();
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        try
        {
            using System.Diagnostics.Process fallback = System.Diagnostics.Process.GetProcessById((int)processId);
            return fallback.ProcessName + ".exe";
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nSize);
}