using System.Threading;
using System.Windows;
using ClipDiff.Windows.Diagnostics;
using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.Installation;

namespace ClipDiff.Windows;

public partial class App : System.Windows.Application
{
	private readonly DiagnosticLog diagnosticLog = new();
	private Mutex? instanceMutex;
	private AppController? controller;
	private bool ownsMutex;
	private InstallerShutdown? installerShutdown;

	protected override void OnStartup(StartupEventArgs args)
	{
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
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
					diagnosticLog.Write(DiagnosticEvent.ExplorerDeliveryFailed);
					System.Windows.MessageBox.Show("ClipDiff could not receive the Explorer comparison. Try the command again.",
						"ClipDiff", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
			}

			instanceMutex.Dispose();
			instanceMutex = null;
			Shutdown();
			return;
		}

		diagnosticLog.Write(DiagnosticEvent.Starting);
		installerShutdown = new InstallerShutdown(Dispatcher, Shutdown);
		controller = new AppController(diagnosticLog);

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
			diagnosticLog.Write(DiagnosticEvent.Stopped);
			instanceMutex?.ReleaseMutex();
			ownsMutex = false;
		}

		instanceMutex?.Dispose();
		instanceMutex = null;
		base.OnExit(args);
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		FlushDiagnosticLog();
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		diagnosticLog.Write(DiagnosticEvent.UnhandledException, exception: args.ExceptionObject as Exception);

		if (args.IsTerminating)
		{
			FlushDiagnosticLog();
		}
	}

	private void FlushDiagnosticLog() => diagnosticLog.CompleteAsync().Wait(TimeSpan.FromMilliseconds(250));
}
