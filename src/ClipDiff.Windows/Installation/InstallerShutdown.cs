using System.Threading;
using System.Windows.Threading;

namespace ClipDiff.Windows.Installation;

internal sealed class InstallerShutdown : IDisposable
{
	// The per-user installer signals this session-local event and waits for the app's mutex to disappear.
	private readonly EventWaitHandle shutdownEvent = new(false, EventResetMode.AutoReset, @"Local\ClipDiff.ShutdownForUpdate");
	private readonly RegisteredWaitHandle registration;

	public InstallerShutdown(Dispatcher dispatcher, Action shutdown)
	{
		registration = ThreadPool.RegisterWaitForSingleObject(shutdownEvent, (_, _) =>
		{
			if (!dispatcher.HasShutdownStarted)
			{
				dispatcher.BeginInvoke(shutdown);
			}
		}, null, Timeout.Infinite, executeOnlyOnce: true);
	}

	public void Dispose()
	{
		registration.Unregister(null);
		shutdownEvent.Dispose();
	}
}
