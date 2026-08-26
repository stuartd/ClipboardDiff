using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ClipDiff.Windows.ViewModels;

internal sealed class DiffWindowViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand _copyCommand;
    private readonly RelayCommand _clearCommand;
    private DiffDocument? _document;
    private int _selectedViewIndex;
    private bool _canClear;

    public DiffWindowViewModel(Action copy, Action clear)
    {
        _copyCommand = new RelayCommand(copy, () => _document is not null);
        _clearCommand = new RelayCommand(clear, () => _canClear);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DiffDocument? Document => _document;

    public IReadOnlyList<DiffRow> Rows => _document?.Rows ?? [];

    public IReadOnlyList<UnifiedLineViewModel> UnifiedLines => _document is null
        ? []
        : CreateUnifiedLines(_document);

    public string Summary => _document is null ? string.Empty : DiffFormatting.Summary(_document.Summary);

    public string PreviousLabel => _document is null
        ? DiffFormatting.DefaultPreviousLabel
        : DiffFormatting.PreviousLabel(_document.Previous);

    public string CurrentLabel => _document is null
        ? DiffFormatting.DefaultCurrentLabel
        : DiffFormatting.CurrentLabel(_document.Current);

    public Visibility EmptyVisibility => _document is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SideBySideVisibility => _document is not null && _selectedViewIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility UnifiedVisibility => _document is not null && _selectedViewIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;

    public int SelectedViewIndex
    {
        get => _selectedViewIndex;
        set
        {
            if (_selectedViewIndex == value)
            {
                return;
            }

            _selectedViewIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SideBySideVisibility));
            OnPropertyChanged(nameof(UnifiedVisibility));
        }
    }

    public RelayCommand CopyCommand => _copyCommand;

    public RelayCommand ClearCommand => _clearCommand;

    public void Load(DiffDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        RaiseDocumentProperties();
    }

    public void ClearDocument()
    {
        _document = null;
        RaiseDocumentProperties();
    }

    public void SetCanClear(bool canClear)
    {
        if (_canClear == canClear)
        {
            return;
        }

        _canClear = canClear;
        _clearCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<UnifiedLineViewModel> CreateUnifiedLines(DiffDocument document)
    {
        var outputLines = DiffFormatting.Unified(document).Split('\n');
        return outputLines.Select((text, index) => new UnifiedLineViewModel(
            text,
            index < 2
                ? UnifiedLineKind.Header
                : text.StartsWith("- ", StringComparison.Ordinal)
                    ? UnifiedLineKind.Removed
                    : text.StartsWith("+ ", StringComparison.Ordinal)
                        ? UnifiedLineKind.Inserted
                        : UnifiedLineKind.Equal)).ToArray();
    }

    private void RaiseDocumentProperties()
    {
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(UnifiedLines));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PreviousLabel));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(SideBySideVisibility));
        OnPropertyChanged(nameof(UnifiedVisibility));
        _copyCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal enum UnifiedLineKind
{
    Header,
    Equal,
    Removed,
    Inserted
}

internal sealed record UnifiedLineViewModel(string Text, UnifiedLineKind Kind);
