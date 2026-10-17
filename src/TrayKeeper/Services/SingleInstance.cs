using System.Threading;

namespace TrayKeeper.Services;

/// <summary>
/// Guarantees one tray icon per user session. A second launch signals the running instance to
/// refresh and then exits, so double-clicking the executable behaves like the menu command
/// rather than adding another icon.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"TrayKeeper.Singleton";
    private const string SignalName = @"Local\TrayKeeper.ActivateSignal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private CancellationTokenSource? _listenerCancellation;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, SignalName);
    }

    /// <summary>Called from a second instance to wake the first one.</summary>
    public void SignalExistingInstance() => _signal.Set();

    /// <summary>
    /// Starts a background listener that invokes <paramref name="onSignal"/> whenever another
    /// instance is launched. The callback is raised on a worker thread.
    /// </summary>
    public void StartListening(Action onSignal)
    {
        _listenerCancellation = new CancellationTokenSource();
        var token = _listenerCancellation.Token;

        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _signal, token.WaitHandle };

            while (!token.IsCancellationRequested)
            {
                // Blocking wait — no polling, so an idle TrayKeeper consumes no CPU.
                if (WaitHandle.WaitAny(handles) == 0) onSignal();
            }
        })
        {
            IsBackground = true,
            Name = "TrayKeeper.InstanceListener",
        };

        thread.Start();
    }

    public void Dispose()
    {
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned on this thread during shutdown; nothing to release.
            }
        }

        _signal.Dispose();
        _mutex.Dispose();
    }
}
