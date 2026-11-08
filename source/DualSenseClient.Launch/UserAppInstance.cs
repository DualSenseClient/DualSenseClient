namespace DualSenseClient.Launch;

/// <summary>A user-scoped instance lock shared by desktop and systemd sessions.</summary>
public sealed class UserAppInstance : IDisposable
{
    private readonly Mutex _mutex;
    public bool Acquired { get; }

    public UserAppInstance(string name = "DualSenseClient")
    {
        _mutex = new Mutex(false, name,
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out _);
        try { Acquired = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { Acquired = true; }
    }

    public void Dispose()
    {
        if (Acquired) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
