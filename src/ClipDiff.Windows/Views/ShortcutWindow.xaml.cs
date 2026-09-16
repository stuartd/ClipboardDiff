using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Views;

public partial class ShortcutWindow : Window
{
    private readonly Func<HotKeyGesture, HotKeyChangeResult> _trySave;
    private HotKeyGesture _gesture;
    private bool _showingModifiers;

    internal ShortcutWindow(
        HotKeyGesture currentGesture,
        Func<HotKeyGesture, HotKeyChangeResult> trySave)
    {
        ArgumentNullException.ThrowIfNull(currentGesture);
        _trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        _gesture = HotKeyGesture.Normalize(currentGesture);
        InitializeComponent();
        ShowGesture();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => ShortcutBox.Focus();

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        var result = NativeMethods.DwmSetWindowAttribute(
            windowHandle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));

        if (result != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }
    }

    private void OnShortcutPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        var key = args.Key switch
        {
            Key.System => args.SystemKey,
            Key.ImeProcessed => args.ImeProcessedKey,
            Key.DeadCharProcessed => args.DeadCharProcessedKey,
            _ => args.Key
        };
        var keyboardModifiers = Keyboard.Modifiers;
        if ((keyboardModifiers & ModifierKeys.Windows) != 0 || key is Key.LWin or Key.RWin)
        {
            args.Handled = true;
            _showingModifiers = false;
            ValidationText.Text = "Windows-key shortcuts aren't supported.";
            SaveButton.IsEnabled = false;
            return;
        }

        var modifiers = ToHotKeyModifiers(keyboardModifiers);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (HotKeyGesture.IsDialogCommand(modifiers, (uint)virtualKey))
        {
            if (_showingModifiers) ShowGesture();
            return;
        }

        args.Handled = true;
        if (IsModifierKey(key))
        {
            _showingModifiers = true;
            ShortcutBox.Text = FormatIncompleteModifiers(modifiers);
            ValidationText.Text = "Press another key to complete the shortcut.";
            SaveButton.IsEnabled = false;
            return;
        }

        _showingModifiers = false;
        if (virtualKey <= 0)
        {
            ValidationText.Text = "That key cannot be used as a ClipDiff shortcut.";
            SaveButton.IsEnabled = false;
            return;
        }

        var candidate = new HotKeyGesture(modifiers, (uint)virtualKey);
        if (!candidate.IsValid)
        {
            ValidationText.Text = (modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt)) == 0
                ? "Include Ctrl or Alt in the shortcut."
                : "That key combination cannot be used as a ClipDiff shortcut.";
            SaveButton.IsEnabled = false;
            return;
        }

        _gesture = candidate;
        ShowGesture();
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs args)
    {
        // Releasing an unfinished modifier chord must not strand the valid
        // draft with Save disabled, including after Shift-Tab moves focus.
        if (_showingModifiers && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowGesture();
        }
    }

    internal bool TryCaptureRegisteredShortcut(HotKeyGesture gesture)
    {
        // RegisterHotKey consumes the key before WPF's capture box sees it.
        if (!IsActive || !ShortcutBox.IsKeyboardFocusWithin)
        {
            return false;
        }

        _gesture = gesture;
        ShowGesture();
        return true;
    }

    private void OnResetClick(object sender, RoutedEventArgs args)
    {
        _gesture = HotKeyGesture.Default;
        ShowGesture();
        ShortcutBox.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs args) => DialogResult = false;

    private void OnSaveClick(object sender, RoutedEventArgs args)
    {
        switch (_trySave(_gesture))
        {
            case HotKeyChangeResult.Success:
                DialogResult = true;
                break;

            case HotKeyChangeResult.Unavailable:
                ValidationText.Text =
                    "Windows couldn't register that shortcut. It may already be used by another application.";
                ShortcutBox.Focus();
                break;

            case HotKeyChangeResult.SaveFailed:
                ValidationText.Text =
                    "ClipDiff couldn't save the shortcut. The previous shortcut has been restored.";
                ShortcutBox.Focus();
                break;

            case HotKeyChangeResult.SaveFailedAndRestoreFailed:
                ValidationText.Text =
                    "ClipDiff couldn't save or restore the previous shortcut. This shortcut is active for this session only.";
                ShortcutBox.Focus();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ShowGesture()
    {
        _showingModifiers = false;
        ShortcutBox.Text = _gesture.DisplayText;
        ValidationText.Text = string.Empty;
        SaveButton.IsEnabled = true;
    }

    private static HotKeyModifiers ToHotKeyModifiers(ModifierKeys modifiers)
    {
        var result = HotKeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= HotKeyModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= HotKeyModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= HotKeyModifiers.Shift;
        }

        return result;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift;

    private static string FormatIncompleteModifiers(HotKeyModifiers modifiers)
    {
        var parts = new List<string>(3);
        if ((modifiers & HotKeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & HotKeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & HotKeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add("…");
        return string.Join('+', parts);
    }
}
