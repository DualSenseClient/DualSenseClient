namespace DualSenseClient.Core.Foreground;

/// <summary>
/// The program in focus: its executable path and foreground window title.
/// </summary>
/// <param name="ExePath">Full executable path of the foreground process, or empty when unknown.</param>
/// <param name="WindowTitle">Title of the foreground window, or empty when unknown.</param>
public readonly record struct ForegroundApp(string ExePath, string WindowTitle);