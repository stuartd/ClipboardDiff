# Agent Guide

Read `SPEC.md` completely before making changes.

ClipDiff is a tiny, privacy-conscious Windows notification-area utility. Keep it native and dependency-light.
Captured text stays in memory except for the explicitly selected external-diff workflow defined in `SPEC.md`, 
which uses short-lived plaintext files with a one-time warning and best-effort cleanup.
Do not otherwise persist, log, upload, or retain captured clipboard text.

Run the relevant tests after changes. Do not expand the product beyond `SPEC.md` without explicit approval.

Formatting rules:

- Tabs not spaces

- Always use braces for conditonals

Example:

if (valid)
   return 
   
Should be:

if (valid)
{
	return
}

- Always have blank lines before and after if, for etc

So rather than this:

var position = 0;
foreach (var range in highlights)
{
    var start = starts[range.Start];
    var end = range.End == starts.Length ? source.Length : starts[range.End];
    Add(position, start, false);
}
Add(position, source.Length, false);

It would be this:

var position = 0;

foreach (var range in highlights)
{
    var start = starts[range.Start];
    var end = range.End == starts.Length ? source.Length : starts[range.End];
    Add(position, start, false);
}

Add(position, source.Length, false);

- Use blank lines to separate chunks of code semantically

Example: here all the code is mashed together

cancellationToken.ThrowIfCancellationRequested();
int oldEnd = oldIndex == oldTokens.Count ? oldUnits.Count : oldTokens[oldIndex].Start;
int newEnd = newIndex == newTokens.Count ? newUnits.Count : newTokens[newIndex].Start;
var matches = Align(oldUnits.GetRange(oldStart, oldEnd - oldStart),
newUnits.GetRange(newStart, newEnd - newStart), ref searchBudget, false);

But here it is separated - cancellation handling, setup, and calculation are separated.

cancellationToken.ThrowIfCancellationRequested();

int oldEnd = oldIndex == oldTokens.Count ? oldUnits.Count : oldTokens[oldIndex].Start;
int newEnd = newIndex == newTokens.Count ? newUnits.Count : newTokens[newIndex].Start;

var matches = Align(oldUnits.GetRange(oldStart, oldEnd - oldStart),
newUnits.GetRange(newStart, newEnd - newStart), ref searchBudget, false);

- Do not add blank lines in if/else blocks:

if (normalize && IsWhitespace(value))
{
	if (units.Count > 0)
	{
		if (units[^1].Text == " ")
		{
			units[^1] = units[^1] with { End = index + 1 };
		}
		else
		{
			units.Add(new(" ", index, index + 1, 1));
		}
	}
}
else
{
	units.Add(new(value, index, index + 1, 1));
}


