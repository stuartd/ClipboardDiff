# Within-line change highlighting for ClipDiff

Implement the within-line highlighting used by MacDiff: in a modified line, give the specific changed text a stronger background so small edits are easy to find. This is a display enhancement to the existing line diff.

## Behaviour

- Apply inline highlights automatically to paired, modified lines.
- Keep the existing subtle whole-line backgrounds and addition/removal markers.
- On the original side, highlight removed or replaced text in stronger red. On the changed side, highlight inserted or replacement text in stronger green.
- Leave matching portions of a modified line with only the normal line background.
- Support multiple separate highlighted spans within one line.
- For an insertion within a paired line, only the changed side needs a span. For a deletion, only the original side needs one. Do not invent a visible placeholder on the empty side.
- Entirely added or removed lines keep their normal whole-line treatment; they do not need additional inline shading. Unchanged lines have no inline spans.
- Preserve line pairing, line numbers, change counts, navigation, wrapping, and selection behaviour.
- Recompute highlights when either input or the ignore-spacing setting changes. No separate highlighting toggle is required.

For example, `timeout = 30` → `timeout = 60` highlights only `3` in red and `6` in green. The common `0` keeps the subtle line background.

## Result data

Extend each diff row with two optional/default-empty collections:

```text
oldHighlights: [Range]
newHighlights: [Range]

Range = { start, end }  // zero-based, start inclusive, end exclusive
```

Ranges refer to extended grapheme clusters in each side's original line, before whitespace normalization or tab expansion. Each list must be ordered, non-overlapping, non-empty per range, and within the source line's bounds. Merge adjacent changed ranges.

Keep the source strings intact. A missing list or an empty list means “no extra inline shading”; it must not change the row's modified status.

If ClipDiff's renderer uses UTF-16 or byte offsets, explicitly convert from grapheme boundaries. Do not treat those offset systems as interchangeable. Emoji sequences and combining marks must never be split by a highlight boundary.

## Matching algorithm

Run the analysis in the existing background comparison pipeline, after lines have been paired. Store the spans on the result rows; do not rerun the diff during painting or scrolling.

1. Convert each line to grapheme units, retaining each unit's range in the original text. Apply the ignore-spacing rules below if enabled.
2. Tokenize these units into consecutive whitespace runs, consecutive word-character runs, and individual other graphemes. Word characters are Unicode letters, numbers, and underscore. Punctuation and emoji are individual tokens.
3. Align equal tokens with a bounded, weighted longest-common-subsequence search. A whitespace token has match weight `1`; every other token has weight `max(2, graphemeCount(token))`. This favours preserving a word over aligning repeated spaces around it.
4. For each unmatched block between token matches, align its graphemes with a bounded, unweighted longest-common-subsequence search. This refines changed words down to the differing characters where possible.
5. Convert the unmatched grapheme spans back to original source ranges for each side, and merge adjacent ranges.

For both searches, strip matching prefixes and suffixes first. Search only the remaining middle. If the search would exceed its work budget, retain those prefix/suffix matches and treat the unmatched middle conservatively as changed. An empty middle on either side needs no search.

For deterministic parity with MacDiff, when two reconstruction paths have equal scores, advance on the original side first. Match the existing line diff's Unicode equality semantics.

Do not align lines differently to improve the inline result. Inline highlighting describes the line pairs already chosen by the main diff.

## Ignore spacing

Use the same meaning as the line-level option:

- Remove leading and trailing whitespace from the comparison representation.
- Collapse each interior whitespace run to one ordinary space.
- Retain a mapping from that normalized space to the complete original whitespace run.
- Display the original text, including its original indentation and tabs.

Whitespace-only differences produce no inline highlights while the option is enabled. Content changes still do. The space separating `a b` is significant when comparing with `ab`; ignore-spacing does not remove all whitespace.

With the option disabled, differences in spaces and tabs are highlighted normally.

## Rendering

Use attributed text or safely escaped text spans, with the source treated as plain text. Keep the existing foreground colour and font.

MacDiff uses a red or green inline background at `28%` opacity over a whole-line background at `12%` opacity. Use these as the starting values, adjusting only if ClipDiff's existing palette requires it. The stronger spans must remain legible in both light and dark appearances.

Slice the original source using its highlight ranges first, then perform display-only tab expansion on each slice. MacDiff renders each tab as four spaces. Highlight all expanded spaces when the source tab is highlighted. Do not calculate offsets from already-expanded display text.

Render the slices as one flowing text layout per side, so highlighting preserves line wrapping, aligned row heights, and text selection. Avoid independent labels or boxes for each span. Copying the complete input must continue to use the unchanged source string.

## Work limits and cancellation

Use these MacDiff defaults for parity:

| Limit | Value |
| --- | --- |
| Combined UTF-8 bytes of a paired line eligible for inline analysis | 16,384 |
| Shared inline work budget per complete comparison | 2,000,000 units |
| Maximum search budget per paired line | 65,536 matrix cells |

Accounting and fallback:

1. If the pair exceeds the byte limit, the shared budget cannot cover its combined byte count, or the task is cancelled, skip its inline analysis.
2. Otherwise, deduct the pair's combined UTF-8 byte count from the shared budget.
3. Set its search budget to `min(65,536, remainingSharedBudget)`. The word search and all character refinements for that pair share this budget.
4. A search of unmatched lengths `n` and `m` costs `(n + 1) × (m + 1)` matrix cells. Check before allocating and deduct only when performing the search.
5. If a search cannot fit, use the matching-prefix/suffix fallback described above. Do not allocate an unbounded matrix.
6. Deduct the pair's consumed search cells from the shared budget. Process modified rows in document order.

Skipped inline analysis leaves the normal whole-line diff visible. Broad highlights are acceptable when a search exhausts its budget. Neither case is an error or warrants a dialog.

Check cancellation between modified rows, between refinement blocks, and during matrix construction. A superseded comparison must never publish its results, including its inline spans. These limits are additional to existing input and line-diff limits.

## Acceptance cases

In the table, `\t` means an actual tab; spaces shown in code are significant. “Spans” lists the exact highlighted substrings in source order.

| Original | Changed | Expected original spans | Expected changed spans |
| --- | --- | --- | --- |
| `timeout = 30` | `timeout = 60` | `3` | `6` |
| `x = 12; y = 34` | `x = 92; y = 38` | `1`, `4` | `9`, `8` |
| `return value;` | `return new_value;` | None | `new_` |
| `return new_value;` | `return value;` | `new_` | None |
| `a\tb` | `a b` | Tab | Space |
| `a\tb` | `a  b`, ignore spacing enabled | None | None |
| `a b` | `ab`, ignore spacing enabled | Space | None |
| `\t let  total = 30  ` | `let total\t= 60`, ignore spacing enabled | `3` | `6` |

Also verify:

- With ignore-spacing disabled, the last example highlights the spacing differences and changed digits without highlighting the unchanged word `total`.
- Changing `👩🏽‍💻` to `👨🏻‍💻` highlights each complete emoji sequence. Changing `e` plus a combining acute accent to `o` plus that accent highlights each complete grapheme.
- A highlighted digit following tabs and emoji still lands on the correct glyph after display expansion.
- A paired empty line changed to `new` highlights `new` only on the changed side.
- Unchanged lines and entirely added/removed lines have no extra inline spans.
- For small exact-mode inputs, removing each side's highlighted spans leaves equal text. Test a reproducible mix of repeated words, punctuation, spaces, tabs, and Unicode.
- Over-limit pairs and an exhausted shared budget preserve all source text and normal line shading. A difficult but eligible pair falls back to broader middle spans.
- Swapping inputs reverses the sides and colours correctly. Rapid edits or option changes cannot leave spans from an earlier comparison.
- Wrapped text, selection, and complete-input copying still work. Highlight backgrounds remain readable in both appearances.

## MacDiff reference implementation

- Matching and bounds: `Sources/DiffCore/InlineDiff.swift`
- Row integration: `Sources/DiffCore/DiffEngine.swift`
- Attributed-text rendering: `Sources/MacDiff/DiffTextFormatting.swift`
- Behaviour tests: `Tests/DiffCoreTests/InlineDiffTests.swift`
- Tab/Unicode rendering tests: `Tests/MacDiffTests/DiffTextFormattingTests.swift`

The implementation can use ClipDiff's existing language and UI components; the behavioural contract and acceptance cases above are the target.
