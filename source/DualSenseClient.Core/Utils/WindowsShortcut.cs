using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Core.Logging;

namespace DualSenseClient.Core.Utils;

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010b-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig]
    int IsDirty();
    void Load([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([In, MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [In, MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([In, MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);
    void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
[ClassInterface(ClassInterfaceType.None)]
internal class ShellLink
{
}

public class WindowsShortcut
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void CreateWindowsShortcut(string shortcutPath, string targetPath, string AppName)
    {
        IShellLinkW? link = null;

        try
        {
            // Ensure the directory exists
            string? directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Logger.Info<WindowsShortcut>($"Created directory: {directory}");
            }

            // Delete existing shortcut if it exists (to avoid file lock issues)
            if (File.Exists(shortcutPath))
            {
                try
                {
                    File.Delete(shortcutPath);
                    Logger.Info<WindowsShortcut>($"Deleted existing shortcut: {shortcutPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warning<WindowsShortcut>($"Could not delete existing shortcut: {ex.Message}");
                }
            }

            // Validate target path exists
            if (!File.Exists(targetPath))
            {
                Logger.Warning<WindowsShortcut>($"Target file does not exist: {targetPath}");
            }

            // Create ShellLink instance
            link = (IShellLinkW)new ShellLink();

            // Set target path
            link.SetPath(targetPath);

            // Set working directory to the executable's directory
            string workingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                link.SetWorkingDirectory(workingDirectory);
            }

            // Set description
            link.SetDescription($"{AppName}");

            // Set icon (use the executable's icon)
            if (File.Exists(targetPath))
            {
                link.SetIconLocation(targetPath, 0);
            }

            // Save the shortcut
            IPersistFile file = (IPersistFile)link;
            file.Save(shortcutPath, false);

            Logger.Info<WindowsShortcut>($"Shortcut created successfully at: {shortcutPath}");
        }
        catch (COMException comEx)
        {
            Logger.Error<WindowsShortcut>($"COM error creating shortcut. HRESULT: 0x{comEx.HResult:X8}");
            Logger.Error<WindowsShortcut>($"Shortcut path: {shortcutPath}");
            Logger.Error<WindowsShortcut>($"Target path: {targetPath}");
            Logger.LogExceptionDetails<WindowsShortcut>(comEx);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error<WindowsShortcut>("Failed to create Windows shortcut");
            Logger.LogExceptionDetails<WindowsShortcut>(ex);
            throw;
        }
        finally
        {
            // Release COM object
            if (link != null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}