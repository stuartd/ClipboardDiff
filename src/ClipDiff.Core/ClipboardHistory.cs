namespace ClipDiff;

public sealed class ClipboardHistory
{
    public static readonly TimeSpan DefaultRecentClearWindow = TimeSpan.FromSeconds(60);

    private readonly List<ClipboardEntry> entries = [];
    private readonly Func<Guid> idFactory;
    private readonly TimeSpan recentClearWindow;
    private ClearEligibility? clearEligibility;
    private uint lastSequenceNumber;

    public ClipboardHistory(
        uint startupSequenceNumber = 0,
        TimeSpan? recentClearWindow = null,
        Func<Guid>? idFactory = null)
    {
        lastSequenceNumber = startupSequenceNumber;
        this.recentClearWindow = recentClearWindow ?? DefaultRecentClearWindow;
        this.idFactory = idFactory ?? Guid.NewGuid;
    }

    public bool IsMonitoring { get; private set; } = true;

    public IReadOnlyList<ClipboardEntry> Entries => [.. entries];

    public ClipboardEntry? Current => entries.Count > 0 ? entries[0] : null;

    public ClipboardEntry? Previous => entries.Count > 1 ? entries[1] : null;

    public uint LastSequenceNumber => lastSequenceNumber;

    public string Status => IsMonitoring
        ? entries.Count switch
        {
            0 => "Waiting for copied text",
            1 => "Copy one more text value",
            _ => "Ready to diff"
        }
        : "Monitoring paused";

    public ClipboardHistoryChange Apply(ClipboardObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!IsMonitoring || observation.SequenceNumber == lastSequenceNumber)
        {
            return ClipboardHistoryChange.None;
        }

        lastSequenceNumber = observation.SequenceNumber;

        switch (observation.Kind)
        {
            case ClipboardObservationKind.Text:
                return ApplyText(observation);

            case ClipboardObservationKind.TextPair:
                return ApplyTextPair(observation);

            case ClipboardObservationKind.ExplicitClear:
                return ApplyExplicitClear(observation.ObservedAt);

            case ClipboardObservationKind.NonText:
            case ClipboardObservationKind.Sensitive:
            case ClipboardObservationKind.InspectionFailed:
            case ClipboardObservationKind.OwnWrite:
                clearEligibility = null;
                return ClipboardHistoryChange.None;

            default:
                throw new ArgumentOutOfRangeException(nameof(observation));
        }
    }

    public ClipboardHistoryChange AcceptDirectText(
        string text,
        DateTimeOffset capturedAt,
        string? sourceFileName = null,
        string? sourceFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!IsMonitoring || text.Length == 0)
        {
            return ClipboardHistoryChange.None;
        }

        return InsertText(
            text,
            capturedAt,
            isEligibleForRecentClear: false,
            sourceFileName: sourceFileName,
            sourceFilePath: sourceFilePath);
    }

    public ClipboardHistoryChange AcceptDirectPair(
        string previousText,
        string currentText,
        DateTimeOffset capturedAt,
        string? previousSourceFileName = null,
        string? currentSourceFileName = null,
        string? previousSourceFilePath = null,
        string? currentSourceFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(previousText);
        ArgumentNullException.ThrowIfNull(currentText);

        if (!IsMonitoring || previousText.Length == 0 || currentText.Length == 0)
        {
            return ClipboardHistoryChange.None;
        }

        return ReplacePair(
            previousText,
            currentText,
            capturedAt,
            isEligibleForRecentClear: false,
            previousSourceFileName,
            currentSourceFileName,
            previousSourceFilePath,
            currentSourceFilePath);
    }

    public void Pause()
    {
        IsMonitoring = false;
        clearEligibility = null;
    }

    public void Resume(uint currentSequenceNumber)
    {
        IsMonitoring = true;
        lastSequenceNumber = currentSequenceNumber;
        clearEligibility = null;
    }

    public void Clear()
    {
        entries.Clear();
        clearEligibility = null;
    }

    private ClipboardHistoryChange ApplyText(ClipboardObservation observation)
    {
        var text = observation.Text ?? throw new ArgumentException(
            "A text observation must contain a text value.", nameof(observation));

        if (text.Length == 0)
        {
            return ApplyExplicitClear(observation.ObservedAt);
        }

        return InsertText(
            text,
            observation.ObservedAt,
            isEligibleForRecentClear: true,
            sourceFileName: observation.SourceFileName,
            sourceFilePath: observation.SourceFilePath);
    }

    private ClipboardHistoryChange InsertText(
        string text,
        DateTimeOffset capturedAt,
        bool isEligibleForRecentClear,
        string? sourceFileName,
        string? sourceFilePath)
    {
        var entry = new ClipboardEntry(
            idFactory(),
            text,
            capturedAt,
            sourceFileName,
            sourceFilePath);
        entries.Insert(0, entry);
        if (entries.Count > 2)
        {
            entries.RemoveRange(2, entries.Count - 2);
        }

        clearEligibility = isEligibleForRecentClear
            ? new(entry.Id, capturedAt)
            : null;
        return ClipboardHistoryChange.Accepted;
    }

    private ClipboardHistoryChange ApplyTextPair(ClipboardObservation observation)
    {
        var previousText = observation.PreviousText ?? throw new ArgumentException(
            "A text-pair observation must contain a previous text value.", nameof(observation));
        var currentText = observation.Text ?? throw new ArgumentException(
            "A text-pair observation must contain a current text value.", nameof(observation));
        if (previousText.Length == 0 || currentText.Length == 0)
        {
            throw new ArgumentException(
                "A text-pair observation cannot contain an empty text value.", nameof(observation));
        }

        return ReplacePair(
            previousText,
            currentText,
            observation.ObservedAt,
            isEligibleForRecentClear: true,
            observation.PreviousSourceFileName,
            observation.SourceFileName,
            observation.PreviousSourceFilePath,
            observation.SourceFilePath);
    }

    private ClipboardHistoryChange ReplacePair(
        string previousText,
        string currentText,
        DateTimeOffset capturedAt,
        bool isEligibleForRecentClear,
        string? previousSourceFileName,
        string? currentSourceFileName,
        string? previousSourceFilePath,
        string? currentSourceFilePath)
    {
        var previous = new ClipboardEntry(
            idFactory(),
            previousText,
            capturedAt,
            previousSourceFileName,
            previousSourceFilePath);
        var current = new ClipboardEntry(
            idFactory(),
            currentText,
            capturedAt,
            currentSourceFileName,
            currentSourceFilePath);
        entries.Clear();
        entries.Add(current);
        entries.Add(previous);
        clearEligibility = isEligibleForRecentClear
            ? new(current.Id, capturedAt)
            : null;
        return ClipboardHistoryChange.Accepted;
    }

    private ClipboardHistoryChange ApplyExplicitClear(DateTimeOffset observedAt)
    {
        if (clearEligibility is not { } eligibility ||
            Current?.Id != eligibility.EntryId ||
            observedAt < eligibility.ObservedAt ||
            observedAt - eligibility.ObservedAt > recentClearWindow)
        {
            clearEligibility = null;
            return ClipboardHistoryChange.None;
        }

        entries.RemoveAt(0);
        clearEligibility = null;
        return ClipboardHistoryChange.RemovedByRecentClear;
    }

    private sealed record ClearEligibility(Guid EntryId, DateTimeOffset ObservedAt);
}
