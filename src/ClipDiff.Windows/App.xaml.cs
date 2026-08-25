using System.Threading;
using System.Windows;
using ClipDiff.Windows.Explorer;

namespace ClipDiff.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private AppController? _controller;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);

        var isExplorerCommand = ExplorerContextCommandLine.TryGetSelectedFile(
            args.Args,
            out var selectedFilePath);

        _instanceMutex = new Mutex(true, @"Local\ClipDiff", out _ownsMutex);
        if (!_ownsMutex)
        {
            if (isExplorerCommand)
            {
                ExplorerCommandClient.TrySendSelectedFile(selectedFilePath);
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _controller = new AppController();
        if (isExplorerCommand)
        {
            _controller.CompareWithCurrent(selectedFilePath);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        _controller?.Dispose();
        _controller = null;

        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
            _ownsMutex = false;
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(args);
    }
}
