#nullable enable
using System;
namespace ModConfigEnhance.Overview;

/// <summary>One observer per view; never treats the queue's uncorrelated global error as a receipt.
/// Commands are not cancelled by replacing/disposing the observer. Only the newest callbacks survive.</summary>
internal sealed class NetworkSubmissionState : IDisposable
{
    internal enum Notice { Submitting, Waiting, Applied, SubmissionFailed, DispatchUncertain }
    private sealed class Attempt
    {
        internal string Mode = "", Address = "", Command = "";
        internal long Baseline;
        internal double Started;
        internal bool Delayed;
        internal Action<Notice, string>? Status;
        internal Action? Applied;
    }
    private Attempt? _current;
    private bool _disposed;
    private long _generation;
    internal bool Pending => _current != null;

    internal void Submit(string mode, string address, long baseline, double now,
        Action<Action<string>> post, Action<Notice, string> status, Action? applied = null)
    {
        if (_disposed) return;
        var attempt = new Attempt { Mode = mode, Address = mode == "custom" ? address.Trim() : "",
            Baseline = baseline, Started = now, Status = status, Applied = applied };
        _generation++;
        _current = attempt; // release old callbacks even if the new dispatch throws
        try
        {
            // The queue calls this immediately after the atomic command-file rename,
            // before launching the worker (which may throw despite successful posting).
            post(command => { if (ReferenceEquals(_current, attempt)) attempt.Command = command; });
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(_current, attempt)) return;
            if (attempt.Command.Length == 0)
            {
                _current = null;
                status(Notice.SubmissionFailed, error.Message); // reliably local to this attempt
            }
            else status(Notice.DispatchUncertain, ""); // posted: keep observing, never claim failure
            return;
        }
        if (ReferenceEquals(_current, attempt)) status(Notice.Submitting, "");
    }

    internal void Observe(long revision, string mode, string address, double now, Func<string, bool> processed)
    {
        var attempt = _current;
        if (attempt == null || _disposed) return;
        // The existing processed-command file binds this observation to the submission.
        // A fresh saved target is the truth; neither global Error nor elapsed time proves failure.
        if (attempt.Command.Length > 0 && revision > attempt.Baseline &&
            mode == attempt.Mode && (mode != "custom" || address.Trim() == attempt.Address) && processed(attempt.Command))
        {
            if (!ReferenceEquals(_current, attempt)) return;
            long generation = _generation;
            _current = null;
            attempt.Applied?.Invoke();
            // An applied callback may have submitted a new target or closed the view.
            if (!_disposed && _generation == generation) attempt.Status?.Invoke(Notice.Applied, "");
            return;
        }
        if (!attempt.Delayed && now - attempt.Started >= 15)
        {
            attempt.Delayed = true;
            attempt.Status?.Invoke(Notice.Waiting, ""); // no deadline: late confirmation still updates dirty baseline
        }
    }
    internal void CancelObservation() { _generation++; _current = null; }
    public void Dispose() { _disposed = true; CancelObservation(); }
}
