using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Views;

public partial class ShortcutWindow : Window
{
    private readonly Func<HotKeyGesture, HotKeyChangeResult> trySave;
    private HotKeyGesture gesture;
    private bool showingModifiers;

    internal ShortcutWindow(
        HotKeyGesture currentGesture,
        Func<HotKeyGesture, HotKeyChangeResult> trySave)
    {
        ArgumentNullException.ThrowIfNull(currentGesture);
        this.trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        gesture = HotKeyGesture.Normalize(currentGesture);
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
            showingModifiers = false;
            ValidationText.Text = "Windows-key shortcuts aren't supported.";
            SaveButton.IsEnabled = false;
            return;
        }

        var modifiers = ToHotKeyModifiers(keyboardModifiers);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (HotKeyGesture.IsDialogCommand(modifiers, (uint)virtualKey))
        {
            if (showingModifiers)
			{
				ShowGesture();
			}

			return;
        }

        args.Handled = true;
        if (IsModifierKey(key))
        {
            showingModifiers = true;
            ShortcutBox.Text = FormatIncompleteModifiers(modifiers);
            ValidationText.Text = "Press another key to complete the shortcut.";
            SaveButton.IsEnabled = false;
            return;
        }

        showingModifiers = false;
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

        gesture = candidate;
        ShowGesture();
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs args)
    {
        // Releasing an unfinished modifier chord must not strand the valid
        // draft with Save disabled, including after Shift-Tab moves focus.
        if (showingModifiers && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowGesture();
        }
    }

    internal bool TryCaptureRegisteredShortcut(HotKeyGesture hotKeyGesture)
    {
        // RegisterHotKey consumes the key before WPF's capture box sees it.
        if (!IsActive || !ShortcutBox.IsKeyboardFocusWithin)
        {
            return false;
        }

        this.gesture = hotKeyGesture;
        ShowGesture();
        return true;
    }

    private void OnResetClick(object sender, RoutedEventArgs args)
    {
        gesture = HotKeyGesture.Default;
        ShowGesture();
        ShortcutBox.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs args) => DialogResult = false;

    private void OnSaveClick(object sender, RoutedEventArgs args)
    {
        switch (trySave(gesture))
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
        showingModifiers = false;
        ShortcutBox.Text = gesture.DisplayText;
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
