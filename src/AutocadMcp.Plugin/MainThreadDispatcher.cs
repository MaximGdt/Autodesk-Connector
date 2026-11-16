using System.Windows.Forms;

namespace AutocadMcp.Plugin;

/// <summary>
/// AutoCAD's API may only be used from AutoCAD's main (UI) thread.
/// Requests arrive on a background pipe thread, so they are marshalled here.
///
/// A hidden WinForms control is created on the main thread at startup; BeginInvoke on it
/// queues work onto that thread. If the work cannot START before the timeout
/// (e.g. AutoCAD is frozen), it is cancelled and will never run later, so a retry
/// from Claude can never produce a duplicate drawing.
/// </summary>
internal sealed class MainThreadDispatcher : IDisposable
{
    private const int Pending = 0;
    private const int Running = 1;
    private const int Expired = 2;

    private readonly Control _control;

    /// <summary>Must be constructed on AutoCAD's main thread (IExtensionApplication.Initialize).</summary>
    public MainThreadDispatcher()
    {
        _control = new Control();
        _control.CreateControl();
        _ = _control.Handle; // force the window handle to exist on this thread
    }

    public async Task<T> InvokeAsync<T>(Func<T> work, TimeSpan timeout)
    {
        var state = Pending;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _control.BeginInvoke(new Action(() =>
        {
            if (Interlocked.CompareExchange(ref state, Running, Pending) != Pending)
            {
                return; // timed out before it started: drop it
            }

            try
            {
                tcs.TrySetResult(work());
            }
            catch (System.Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }));

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished == tcs.Task)
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        if (Interlocked.CompareExchange(ref state, Expired, Pending) == Pending)
        {
            throw new TimeoutException("AutoCAD не начал выполнять запрос вовремя (занят или открыт диалог).");
        }

        // Already running: let it finish so the caller gets the real result.
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Dispose() => _control.Dispose();
}
