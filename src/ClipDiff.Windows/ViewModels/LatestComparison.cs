namespace ClipDiff.Windows.ViewModels;

// Called on the UI thread. Await resumes there, so the identity check and publication
// happen together; even a worker that finishes just before cancellation cannot publish.
internal sealed class LatestComparison
{
    private CancellationTokenSource? active;

    public void Cancel()
    {
        active?.Cancel();
        active = null;
    }

    public async Task RunAsync(Func<CancellationToken, Task<DiffDocument>> compare, Action<DiffDocument> publish)
    {
        Cancel();
        using var cancellation = new CancellationTokenSource();
        active = cancellation;
        try
        {
            var document = await compare(cancellation.Token);
            if (ReferenceEquals(active, cancellation) && !cancellation.IsCancellationRequested)
			{
				publish(document);
			}
		}
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded, cleared, or shutting down.
        }
        finally
        {
            if (ReferenceEquals(active, cancellation))
			{
				active = null;
			}
		}
    }
}
