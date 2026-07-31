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
        if (TryPost(invoker, action))
            return;

        ResetInvoker(invoker);
        invoker = EnsureInvoker();
        if (!TryPost(invoker, action))
        {
            ResetInvoker(invoker);
            throw new InvalidOperationException("Capture overlay thread is not accepting work.");
        }
    }

    public static void Stop()
    {
        Control? invoker;
        Thread? thread;
        ManualResetEventSlim? ready;
        lock (Sync)
        {
            invoker = _invoker;
            thread = _thread;
            ready = _ready;
            _invoker = null;
            _thread = null;
            _ready = null;
        }

        try
        {
            if (invoker is { IsDisposed: false, IsHandleCreated: true } usableInvoker)
                usableInvoker.BeginInvoke(new Action(System.Windows.Forms.Application.ExitThread));
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "capture.overlay-thread.stop",
                "Could not post the shutdown request to the capture overlay thread.",
                ex);
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            try
            {
                if (!thread.Join(1500))
                {
                    AppDiagnostics.LogWarning(
                        "capture.overlay-thread.stop-timeout",
                        "The capture overlay thread did not stop within 1.5 seconds.");
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "capture.overlay-thread.stop-wait",
                    "Could not wait for the capture overlay thread to stop.",
                    ex);
            }
        }

        try
        {
            ready?.Dispose();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "capture.overlay-thread.dispose-ready",
                "Could not dispose the capture overlay readiness signal.",
                ex);
        }
    }

    private static Control EnsureInvoker()
    {
        ManualResetEventSlim ready;
        lock (Sync)
        {
            if (_invoker is { IsDisposed: false, IsHandleCreated: true } existingInvoker)
                return existingInvoker;

            if (_thread is { IsAlive: true } && _ready is { IsSet: false } pendingReady)
            {
                ready = pendingReady;
            }
            else
            {
                ready = new ManualResetEventSlim(false);
                _ready = ready;
                _thread = new Thread(() => ThreadMain(ready))
                {
                    IsBackground = true,
                    Name = "OddSnap capture overlay"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        if (!ready.Wait(TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Timed out starting the capture overlay thread.");

        lock (Sync)
        {
            if (_invoker is { IsDisposed: false, IsHandleCreated: true } existingInvoker)
                return existingInvoker;

            if (ReferenceEquals(_ready, ready))
            {
                _thread = null;
                _ready = null;
            }
        }

        throw new InvalidOperationException("Capture overlay thread did not initialize.");
    }

    private static void ThreadMain(ManualResetEventSlim ready)
    {
        Control? invoker = null;
        try
        {
            invoker = new Control();
            _ = invoker.Handle;

            bool isCurrentThread;
            lock (Sync)
            {
                isCurrentThread = ReferenceEquals(_ready, ready);
                if (isCurrentThread)
                {
                    _invoker = invoker;
                    ready.Set();
                }
            }

            if (!isCurrentThread)
                return;

            System.Windows.Forms.Application.Run();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("capture.overlay-thread.run", ex);
        }
        finally
        {
            lock (Sync)
            {
                if (ReferenceEquals(_invoker, invoker))
                    _invoker = null;
                if (ReferenceEquals(_ready, ready))
                {
                    try
                    {
                        ready.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Stop may already have disposed a readiness signal it detached.
                    }
                    _ready = null;
                }
                if (ReferenceEquals(_thread, Thread.CurrentThread))
                    _thread = null;
            }

            try
            {
                invoker?.Dispose();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning(
                    "capture.overlay-thread.dispose-invoker",
                    "Could not dispose the capture overlay thread invoker.",
                    ex);
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
            {
                _invoker = null;
                _thread = null;
                _ready = null;
            }
        }
    }

    private static bool TryPost(Control invoker, Action action)
    {
        if (!IsInvokerUsable(invoker))
            return false;

        try
        {
            invoker.BeginInvoke(new Action(() => InvokeAction(action)));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsInvokerUsable(Control? invoker) =>
        invoker is { IsDisposed: false, IsHandleCreated: true };
}
