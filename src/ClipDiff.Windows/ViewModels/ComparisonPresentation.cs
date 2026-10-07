namespace ClipDiff.Windows.ViewModels;

// UI-thread only. Native confirmation dialogs run a nested message loop, so
// clipboard invalidation and a newer Show Diff can occur before they return.
internal sealed class ComparisonPresentation
{
	private long generation;

	public void Cancel() => generation++;

	public void Show(Func<bool>? confirm, Func<bool> launch, Action showBuiltIn)
	{
		var request = ++generation;

		if (confirm is not null)
		{
			var confirmed = confirm();

			if (request != generation)
			{
				return;
			}

			if (confirmed && launch())
			{
				return;
			}
		}

		if (request == generation)
		{
			showBuiltIn();
		}
	}
}
