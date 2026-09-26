using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ClipDiff.Windows.ViewModels;

internal sealed class DiffWindowViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand copyCommand;
    private readonly RelayCommand clearCommand;
    private DiffDocument? document;
    private int selectedViewIndex;
    private bool canClear;
    private bool ignoreSpacing;
    private readonly Action comparisonOptionsChanged;

    public DiffWindowViewModel(Action copy, Action clear, Action comparisonOptionsChanged)
    {
        this.comparisonOptionsChanged = comparisonOptionsChanged;
        copyCommand = new RelayCommand(copy, () => document is not null);
        clearCommand = new RelayCommand(clear, () => canClear);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DiffDocument? Document => document;

    public IReadOnlyList<DiffRow> Rows => document?.Rows ?? [];

    public IReadOnlyList<UnifiedLineViewModel> UnifiedLines => document is null
        ? []
        : CreateUnifiedLines(document);

    public string Summary => document is null ? string.Empty : DiffFormatting.Summary(document.Summary);

    public string PreviousLabel => document is null
        ? DiffFormatting.DefaultPreviousLabel
        : document.Labels.Previous;

    public string CurrentLabel => document is null
        ? DiffFormatting.DefaultCurrentLabel
        : document.Labels.Current;

    public Visibility EmptyVisibility => document is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SideBySideVisibility => document is not null && selectedViewIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility UnifiedVisibility => document is not null && selectedViewIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;

    public int SelectedViewIndex
    {
        get => selectedViewIndex;
        set
        {
            if (selectedViewIndex == value)
            {
                return;
            }

            selectedViewIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SideBySideVisibility));
            OnPropertyChanged(nameof(UnifiedVisibility));
        }
    }

    public bool IgnoreSpacing
    {
        get => ignoreSpacing;
        set
        {
            if (ignoreSpacing == value)
			{
				return;
			}

			ignoreSpacing = value;
            OnPropertyChanged();
            comparisonOptionsChanged();
        }
    }

    public RelayCommand CopyCommand => copyCommand;

    public RelayCommand ClearCommand => clearCommand;

    public void Load(DiffDocument diffDocument)
    {
        this.document = diffDocument ?? throw new ArgumentNullException(nameof(diffDocument));
        RaiseDocumentProperties();
    }

    public void ClearDocument()
    {
        document = null;
        RaiseDocumentProperties();
    }

    public void SetCanClear(bool canClearValue)
    {
        if (this.canClear == canClearValue)
        {
            return;
        }

        this.canClear = canClearValue;
        clearCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<UnifiedLineViewModel> CreateUnifiedLines(DiffDocument document)
    {
        var lines = new List<UnifiedLineViewModel>
        {
            new(document.Labels.Previous, UnifiedLineKind.Header, "--- ", []),
            new(document.Labels.Current, UnifiedLineKind.Header, "+++ ", []),
		};
        foreach (var row in document.Rows)
        {
            if (row.Kind == DiffKind.Equal)
			{
				lines.Add(new(row.OldText, UnifiedLineKind.Equal, "  ", []));
			}

			if (row.Kind is DiffKind.Removed or DiffKind.Changed)
			{
				lines.Add(new(row.OldText, UnifiedLineKind.Removed, "- ", row.OldHighlights));
			}

			if (row.Kind is DiffKind.Inserted or DiffKind.Changed)
			{
				lines.Add(new(row.NewText, UnifiedLineKind.Inserted, "+ ", row.NewHighlights));
			}
		}
        return lines;
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
        copyCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal enum UnifiedLineKind
{
    Header,
    Equal,
    Removed,
    Inserted,
}

internal sealed record UnifiedLineViewModel(string? Text, UnifiedLineKind Kind, string Prefix, IReadOnlyList<HighlightRange> Highlights);
