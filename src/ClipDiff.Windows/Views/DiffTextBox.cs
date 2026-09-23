using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace ClipDiff.Windows.Views;

/// <summary>A selectable, wrapping paragraph with plain-text runs and precomputed highlights.</summary>
public sealed class DiffTextBox : RichTextBox
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(DiffTextBox), new PropertyMetadata(null, OnTextChanged));
    public static readonly DependencyProperty HighlightsProperty = DependencyProperty.Register(
        nameof(Highlights), typeof(IReadOnlyList<HighlightRange>), typeof(DiffTextBox), new PropertyMetadata(null, OnTextChanged));
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.Register(
        nameof(Prefix), typeof(string), typeof(DiffTextBox), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(DiffTextBox), new PropertyMetadata(Brushes.Transparent, OnTextChanged));

    public string? SourceText { get => (string?)GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }
    public IReadOnlyList<HighlightRange>? Highlights { get => (IReadOnlyList<HighlightRange>?)GetValue(HighlightsProperty); set => SetValue(HighlightsProperty, value); }
    public string Prefix { get => (string)GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public Brush HighlightBrush { get => (Brush)GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    public DiffTextBox()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        IsDocumentEnabled = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private bool _updatePending;

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var box = (DiffTextBox)sender;
        if (box._updatePending) return;
        box._updatePending = true;
        // Recycling updates source and ranges through separate bindings. Wait for all
        // data bindings before slicing, so ranges can never address the previous row.
        box.Dispatcher.InvokeAsync(() =>
        {
            box._updatePending = false;
            box.UpdateRuns();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void UpdateRuns()
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(new Run(Prefix));
        foreach (var slice in DiffTextFormatting.Slices(SourceText, Highlights ?? []))
        {
            var run = new Run(slice.Text);
            if (slice.Highlighted)
                run.Background = HighlightBrush;
            paragraph.Inlines.Add(run);
        }
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
    }
}
