using System.Windows.Forms;
using OddSnap.Services;

namespace OddSnap.Capture;

internal static class CaptureOverlayThread
{
    private static readonly object Sync = new();
    private static Thread? _thread;
    private static Control? _invoker;
    private static ManualResetEventSlim? _ready;

    public static void Start()
    {
        _ = EnsureInvoker();
    }

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var invoker = EnsureInvoker();
        try
        {
            invoker.BeginInvoke(new Action(() => InvokeAction(action)));
        }
        catch (InvalidOperationException)
        {
            ResetInvoker(invoker);
            EnsureInvoker().BeginInvoke(new Action(() => InvokeAction(action)));
        }
    }

    public static void Stop()
    {
        Control? invoker;
        lock (Sync)
        {
            invoker = _invoker;
            _invoker = null;
            _thread = null;
            _ready = null;
        }

        try
        {
            if (invoker is { IsDisposed: false })
                invoker.BeginInvoke(new Action(System.Windows.Forms.Application.ExitThread));
        }
        catch { }
    }

    private static Control EnsureInvoker()
    {
        ManualResetEventSlim ready;
        lock (Sync)
        {
            if (_invoker is { IsDisposed: false })
                return _invoker;

            ready = new ManualResetEventSlim(false);
            _ready = ready;
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "OddSnap capture overlay"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start(ready);
        }

        if (!ready.Wait(TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Timed out starting the capture overlay thread.");

        lock (Sync)
        {
            if (_invoker is { IsDisposed: false })
                return _invoker;
        }

        throw new InvalidOperationException("Capture overlay thread did not initialize.");
    }

    private static void ThreadMain(object? state)
    {
        var ready = (ManualResetEventSlim)state!;
        using var invoker = new Control();
        _ = invoker.Handle;

        lock (Sync)
        {
            _invoker = invoker;
        }

        ready.Set();

        try
        {
            System.Windows.Forms.Application.Run();
        }
        finally
        {
            lock (Sync)
            {
                if (ReferenceEquals(_invoker, invoker))
                    _invoker = null;
            }
        }
    }

    private static void InvokeAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("capture.overlay-thread.action", ex);
        }
    }

    private static void ResetInvoker(Control invoker)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_invoker, invoker))
                _invoker = null;
            _thread = null;
            _ready = null;
        }
    }
}
