namespace PowerPete.Analyzer.Pipeline;

using PowerPete.Analyzer.Domain;

/// <summary>
/// Says what a stage is doing, while it is doing it.
/// </summary>
/// <remarks>
/// Fire and forget, on purpose. A progress note is the least important thing a run writes:
/// it must never slow a stage down, never fail one, and never be waited for. A note that is
/// dropped costs a second of staleness on a screen that refreshes anyway.
///
/// Two things keep it cheap. Nothing is written more often than once a second, because a
/// loop over ninety thousand components would otherwise be a database write per component.
/// And a note that arrives while another is still being written is discarded rather than
/// queued, because the newer one would replace it immediately in any case.
/// </remarks>
/// <param name="journal">Where stage rows are kept.</param>
/// <param name="runId">Which run.</param>
/// <param name="stageId">Which stage.</param>
public sealed class StageProgress(IRunJournal journal, Guid runId, string stageId) : IProgress<StageNote>
{
    /// <summary>How rarely a note is written.</summary>
    private static readonly TimeSpan Quietest = TimeSpan.FromSeconds(1);

    /// <summary>One at a time. A second note while the first is in flight is dropped.</summary>
    /// <remarks>
    /// A flag rather than a semaphore, so nothing here has to be disposed. This object is
    /// created once per stage and replaced by the next one, and a reporter that had to be
    /// cleaned up would be a lifetime to get wrong for the sake of a progress note.
    /// </remarks>
    private int writing;

    private long lastTicks;

    /// <summary>Records what the stage is doing now.</summary>
    /// <param name="value">The note.</param>
    public void Report(StageNote value)
    {
        if (value is null) return;

        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref lastTicks) < Quietest.Ticks) return;
        if (Interlocked.CompareExchange(ref writing, 1, 0) != 0) return;

        Interlocked.Exchange(ref lastTicks, now);

        _ = WriteAsync(value.ToJson());
    }

    /// <summary>Writes it, and swallows whatever went wrong.</summary>
    /// <param name="note">The note as JSON.</param>
    private async Task WriteAsync(string note)
    {
        try
        {
            await journal.SetStageProgressAsync(runId, stageId, note, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Nothing about a progress note is worth failing a run over.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Deliberately silent. The alternative is a run that dies at the point where it
            // was telling somebody how well it was going.
        }
        finally
        {
            Interlocked.Exchange(ref writing, 0);
        }
    }
}

/// <summary>
/// A reporter that says nothing.
/// </summary>
/// <remarks>
/// The default on a run's state, so a test can run a stage without a database behind it and
/// so a stage never has to check whether anybody is listening.
/// </remarks>
public sealed class NoProgress : IProgress<StageNote>
{
    /// <summary>The one instance there needs to be.</summary>
    public static readonly NoProgress Instance = new();

    /// <summary>Does nothing.</summary>
    /// <param name="value">Ignored.</param>
    public void Report(StageNote value)
    {
    }
}
