using System.Threading;
using System.Windows;
using ClipDiff.Windows.Explorer;

namespace ClipDiff.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private AppController? controller;
    private bool ownsMutex;

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
                ExplorerCommandClient.TrySendSelectedFile(selectedFilePath);
            }

            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown();
            return;
        }

        controller = new AppController();
        if (isExplorerCommand)
        {
            controller.CompareWithCurrent(selectedFilePath);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
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
