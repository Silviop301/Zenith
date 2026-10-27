namespace Zenith.App.Tray;

/// <summary>
/// One Zenith per user session. A second start (the Start menu while it sits in the tray, say) asks the first one
/// to show its window and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string Name = @"Local\Zenith.App";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show, Action onShow)
    {
        _mutex = mutex;
        _show = show;
        _wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Null when another Zenith is already running; it was asked to show itself.</summary>
    public static SingleInstance? TryClaim(Action onShow)
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Show");
        if (first) return new SingleInstance(mutex, show, onShow);

        show.Set();
        show.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
