using System.Threading;
using System.Windows;
using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.Installation;

namespace ClipDiff.Windows;

public partial class App : System.Windows.Application
{
	private Mutex? instanceMutex;
	private AppController? controller;
	private bool ownsMutex;
	private InstallerShutdown? installerShutdown;

	protected override void OnStartup(StartupEventArgs args)
	{
		base.OnStartup(args);

		var isExplorerCommand = ExplorerContextCommandLine.TryGetSelectedFile(
			args.Args,
			out var selectedFilePath);

		instanceMutex = new Mutex(true, @"Local\ClipDiff", out ownsMutex);
		if (!ownsMutex)
		{
			if (isExplorerCommand)
			{
				if (!ExplorerCommandClient.TrySendSelectedFile(selectedFilePath))
				{
					System.Windows.MessageBox.Show("ClipDiff could not receive the Explorer comparison. Try the command again.",
						"ClipDiff", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
			}

			instanceMutex.Dispose();
			instanceMutex = null;
			Shutdown();
			return;
		}

		installerShutdown = new InstallerShutdown(Dispatcher, Shutdown);
		controller = new AppController();
		if (isExplorerCommand)
		{
			controller.CompareWithCurrent(selectedFilePath);
		}
		else
		{
			Dispatcher.InvokeAsync(() =>
			{
				controller.InitializeStartAtLogin();
				controller?.InitializeUpdateConsent();
			});
		}
	}

	protected override void OnExit(ExitEventArgs args)
	{
		installerShutdown?.Dispose();
		installerShutdown = null;
		controller?.Dispose();
		controller = null;

		if (ownsMutex)
		{
			instanceMutex?.ReleaseMutex();
			ownsMutex = false;
		}

		instanceMutex?.Dispose();
		instanceMutex = null;
		base.OnExit(args);
	}
}
