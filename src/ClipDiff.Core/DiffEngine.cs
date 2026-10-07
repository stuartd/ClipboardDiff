namespace ClipDiff;

public sealed class DiffEngine
{
	private const long MaximumTraceBytes = 8 * 1024 * 1024;
	private const int MaximumSearchWork = 2_000_000;
	private readonly Func<Guid> idFactory;

	public DiffEngine(Func<Guid>? generateId = null)
	{
		idFactory = generateId ?? Guid.NewGuid;
	}

	public DiffDocument Compare(
		ClipboardEntry previous,
		ClipboardEntry current,
		DateTimeOffset? createdAt = null,
		bool ignoreSpacing = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(previous);
		ArgumentNullException.ThrowIfNull(current);

		cancellationToken.ThrowIfCancellationRequested();

		var oldLines = TextLines.Split(previous.Text);
		var newLines = TextLines.Split(current.Text);
		var oldKeys = ignoreSpacing ? oldLines.Select(InlineDiff.NormalizeSpacing).ToArray() : oldLines;
		var newKeys = ignoreSpacing ? newLines.Select(InlineDiff.NormalizeSpacing).ToArray() : newLines;

		var edits = ShortestEditScript(oldKeys, newKeys, cancellationToken);

		// Restore original text after matching normalized keys; display and copy never normalize it.
		edits = edits.Select(edit => edit with
		{
			OldText = edit.OldIndex < 0 ? null : oldLines[edit.OldIndex],
			NewText = edit.NewIndex < 0 ? null : newLines[edit.NewIndex]
		}).ToArray();
		var inline = new InlineDiff(ignoreSpacing, cancellationToken);
		var rows = GenerateRows(edits, cancellationToken).Select(inline.Highlight).ToArray();

		cancellationToken.ThrowIfCancellationRequested();

		var summary = new DiffSummary(
			rows.Count(row => row.Kind == DiffKind.Inserted),
			rows.Count(row => row.Kind == DiffKind.Removed),
			rows.Count(row => row.Kind == DiffKind.Changed),
			rows.Count(row => row.Kind == DiffKind.Equal));
		var labels = DiffFormatting.Labels(previous, current);

		return new DiffDocument(
			idFactory(),
			previous with { SourceFilePath = null },
			current with { SourceFilePath = null },
			rows,
			summary,
			createdAt ?? DateTimeOffset.Now,
			labels);
	}

	private IReadOnlyList<DiffRow> GenerateRows(IReadOnlyList<Edit> edits, CancellationToken cancellationToken)
	{
		var rows = new List<DiffRow>();
		var index = 0;

		while (index < edits.Count)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (edits[index].Kind == EditKind.Equal)
			{
				var equal = edits[index++];
				rows.Add(new DiffRow(
					idFactory(),
					equal.OldIndex + 1,
					equal.NewIndex + 1,
					equal.OldText,
					equal.NewText,
					DiffKind.Equal));
				continue;
			}

			var removed = new List<Edit>();
			var inserted = new List<Edit>();

			while (index < edits.Count && edits[index].Kind != EditKind.Equal)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var edit = edits[index++];

				if (edit.Kind == EditKind.Removed)
				{
					removed.Add(edit);
				}
				else
				{
					inserted.Add(edit);
				}
			}

			var pairedCount = Math.Min(removed.Count, inserted.Count);

			for (var pair = 0; pair < pairedCount; pair++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				rows.Add(new DiffRow(
					idFactory(),
					removed[pair].OldIndex + 1,
					inserted[pair].NewIndex + 1,
					removed[pair].OldText,
					inserted[pair].NewText,
					DiffKind.Changed));
			}

			for (var oldIndex = pairedCount; oldIndex < removed.Count; oldIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var edit = removed[oldIndex];
				rows.Add(new DiffRow(
					idFactory(),
					edit.OldIndex + 1,
					null,
					edit.OldText,
					null,
					DiffKind.Removed));
			}

			for (var newIndex = pairedCount; newIndex < inserted.Count; newIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var edit = inserted[newIndex];
				rows.Add(new DiffRow(
					idFactory(),
					null,
					edit.NewIndex + 1,
					null,
					edit.NewText,
					DiffKind.Inserted));
			}
		}

		return rows;
	}

	private static IReadOnlyList<Edit> ShortestEditScript(string[] oldLines, string[] newLines, CancellationToken cancellationToken)
	{
		var oldCount = oldLines.Length;
		var newCount = newLines.Length;
		var max = checked(oldCount + newCount);
		var frontierLength = ((long)max * 2) + 3;
		var frontierBytes = frontierLength * sizeof(int);

		if (frontierBytes > MaximumTraceBytes)
		{
			return CoarseEditScript(oldLines, newLines, cancellationToken);
		}

		var offset = max + 1;
		var frontier = new int[(int)frontierLength];
		var trace = new List<int[]>();
		var traceBytes = frontierBytes;
		var remainingWork = MaximumSearchWork;
		frontier[offset + 1] = 0;

		for (var distance = 0; distance <= max; distance++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Account for the working frontier and every retained backtracking snapshot.
			if (traceBytes + frontierBytes > MaximumTraceBytes)
			{
				return CoarseEditScript(oldLines, newLines, cancellationToken);
			}

			for (var diagonal = -distance; diagonal <= distance; diagonal += 2)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (remainingWork-- == 0)
				{
					return CoarseEditScript(oldLines, newLines, cancellationToken);
				}

				var frontierIndex = offset + diagonal;
				int oldIndex;

				if (diagonal == -distance ||
					(diagonal != distance && frontier[frontierIndex - 1] < frontier[frontierIndex + 1]))
				{
					oldIndex = frontier[frontierIndex + 1];
				}
				else
				{
					oldIndex = frontier[frontierIndex - 1] + 1;
				}

				var newIndex = oldIndex - diagonal;

				while (oldIndex < oldCount && newIndex < newCount)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (remainingWork-- == 0)
					{
						return CoarseEditScript(oldLines, newLines, cancellationToken);
					}

					if (!string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
					{
						break;
					}

					oldIndex++;
					newIndex++;
				}

				frontier[frontierIndex] = oldIndex;

				if (oldIndex >= oldCount && newIndex >= newCount)
				{
					trace.Add((int[])frontier.Clone());
					return Backtrack(trace, distance, offset, oldLines, newLines, cancellationToken);
				}
			}

			trace.Add((int[])frontier.Clone());
			traceBytes += frontierBytes;
		}

		throw new InvalidOperationException("Unable to compute a line difference.");
	}

	private static IReadOnlyList<Edit> CoarseEditScript(
		string[] oldLines,
		string[] newLines,
		CancellationToken cancellationToken)
	{
		// A bounded search may miss internal matches, but never omits or changes source lines.
		var prefix = 0;

		while (prefix < oldLines.Length && prefix < newLines.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
			{
				break;
			}

			prefix++;
		}

		var oldEnd = oldLines.Length;
		var newEnd = newLines.Length;

		while (oldEnd > prefix && newEnd > prefix)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!string.Equals(oldLines[oldEnd - 1], newLines[newEnd - 1], StringComparison.Ordinal))
			{
				break;
			}

			oldEnd--;
			newEnd--;
		}

		var edits = new List<Edit>(oldLines.Length + newLines.Length);

		for (var index = 0; index < prefix; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			edits.Add(Edit.Equal(index, index, oldLines[index]));
		}

		for (var index = prefix; index < oldEnd; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			edits.Add(Edit.Removed(index, oldLines[index]));
		}

		for (var index = prefix; index < newEnd; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			edits.Add(Edit.Inserted(index, newLines[index]));
		}

		while (oldEnd < oldLines.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();

			edits.Add(Edit.Equal(oldEnd, newEnd, oldLines[oldEnd]));
			oldEnd++;
			newEnd++;
		}

		return edits;
	}

	private static IReadOnlyList<Edit> Backtrack(
		IReadOnlyList<int[]> trace,
		int distance,
		int offset,
		string[] oldLines,
		string[] newLines,
		CancellationToken cancellationToken)
	{
		var edits = new List<Edit>(oldLines.Length + newLines.Length);
		var oldIndex = oldLines.Length;
		var newIndex = newLines.Length;

		for (var depth = distance; depth > 0; depth--)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var previousFrontier = trace[depth - 1];
			var diagonal = oldIndex - newIndex;
			int previousDiagonal;

			if (diagonal == -depth ||
				(diagonal != depth &&
				previousFrontier[offset + diagonal - 1] < previousFrontier[offset + diagonal + 1]))
			{
				previousDiagonal = diagonal + 1;
			}
			else
			{
				previousDiagonal = diagonal - 1;
			}

			var previousOldIndex = previousFrontier[offset + previousDiagonal];
			var previousNewIndex = previousOldIndex - previousDiagonal;

			while (oldIndex > previousOldIndex && newIndex > previousNewIndex)
			{
				cancellationToken.ThrowIfCancellationRequested();

				oldIndex--;
				newIndex--;
				edits.Add(Edit.Equal(oldIndex, newIndex, oldLines[oldIndex]));
			}

			if (oldIndex == previousOldIndex)
			{
				newIndex--;
				edits.Add(Edit.Inserted(newIndex, newLines[newIndex]));
			}
			else
			{
				oldIndex--;
				edits.Add(Edit.Removed(oldIndex, oldLines[oldIndex]));
			}
		}

		while (oldIndex > 0 && newIndex > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			oldIndex--;
			newIndex--;
			edits.Add(Edit.Equal(oldIndex, newIndex, oldLines[oldIndex]));
		}

		while (oldIndex > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			oldIndex--;
			edits.Add(Edit.Removed(oldIndex, oldLines[oldIndex]));
		}

		while (newIndex > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			newIndex--;
			edits.Add(Edit.Inserted(newIndex, newLines[newIndex]));
		}

		edits.Reverse();

		return edits;
	}

	private enum EditKind
	{
		Equal,
		Inserted,
		Removed
	}

	private sealed record Edit(
		EditKind Kind,
		int OldIndex,
		int NewIndex,
		string? OldText,
		string? NewText)
	{
		public static Edit Equal(int oldIndex, int newIndex, string text) =>
			new(EditKind.Equal, oldIndex, newIndex, text, text);

		public static Edit Inserted(int newIndex, string text) =>
			new(EditKind.Inserted, -1, newIndex, null, text);

		public static Edit Removed(int oldIndex, string text) =>
			new(EditKind.Removed, oldIndex, -1, text, null);
	}
}
