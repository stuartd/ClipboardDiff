using System.Threading;
using System.Windows;

namespace ClipDiff.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private AppController? _controller;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);

        _instanceMutex = new Mutex(true, @"Local\ClipDiff", out _ownsMutex);
        if (!_ownsMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _controller = new AppController();
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
