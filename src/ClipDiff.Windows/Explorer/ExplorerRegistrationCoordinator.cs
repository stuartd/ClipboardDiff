using System.IO;
using ClipDiff.Windows.Ownership;

namespace ClipDiff.Windows.Explorer;

internal sealed record ExplorerRegistrationClaim(
	string OwnerId,
	bool SingleEnabled,
	string DisplayName,
	string SingleCommandLine,
	string IconPath,
	bool PairEnabled,
	string ComServerCommandLine,
	string ExtensionPath);

internal sealed record ExplorerRegistrationSelection(
	ExplorerRegistrationClaim? Single,
	ExplorerRegistrationClaim? Pair,
	bool MultipleOwners);

internal interface IExplorerRegistrationClaimStore
{
	IReadOnlyList<ExplorerRegistrationClaim> ReadClaims();

	void WriteClaim(ExplorerRegistrationClaim claim);

	void DeleteClaim(string ownerId);
}

internal sealed class ExplorerRegistrationCoordinator : IDisposable
{
	private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromMilliseconds(500);
	private readonly IExplorerRegistrationClaimStore claimStore;
	private readonly string rootDirectory;
	private readonly SharedFileLease sharedFileLease;
	private bool disposed;

	public ExplorerRegistrationCoordinator(IExplorerRegistrationClaimStore store, string directory)
	{
		claimStore = store;
		rootDirectory = directory;
		OwnerId = Guid.NewGuid().ToString("N");
		sharedFileLease = SharedFileLease.Acquire(GetOwnerPath(OwnerId));
	}

	public string OwnerId { get; }

	public bool Update(ExplorerRegistrationClaim claim, Func<ExplorerRegistrationSelection, bool> apply)
	{
		if (disposed)
		{
			return false;
		}

		using var gate = SharedFileLease.TryAcquire(
			Path.Combine(rootDirectory, "coordination.lock"),
			CoordinationTimeout);

		if (gate is null)
		{
			return false;
		}

		claimStore.WriteClaim(claim with { OwnerId = OwnerId });
		return apply(SelectLiveClaims());
	}

	public bool Remove(Func<ExplorerRegistrationSelection, bool> apply)
	{
		if (disposed)
		{
			return false;
		}

		try
		{
			using var gate = SharedFileLease.TryAcquire(
			Path.Combine(rootDirectory, "coordination.lock"),
			CoordinationTimeout);

			if (gate is null)
			{
				return false;
			}

			claimStore.DeleteClaim(OwnerId);
			return apply(SelectLiveClaims());
		}
		finally
		{
			Dispose();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		sharedFileLease.Dispose();
	}

	private ExplorerRegistrationSelection SelectLiveClaims()
	{
		var liveClaims = new List<ExplorerRegistrationClaim>();

		foreach (var claim in claimStore.ReadClaims().OrderBy(claim => claim.OwnerId, StringComparer.Ordinal))
		{
			if (!Guid.TryParseExact(claim.OwnerId, "N", out _))
			{
				claimStore.DeleteClaim(claim.OwnerId);
				continue;
			}

			if (claim.OwnerId == OwnerId && !disposed)
			{
				liveClaims.Add(claim);
				continue;
			}

			using var staleOwnership = SharedFileLease.TryAcquire(GetOwnerPath(claim.OwnerId));

			if (staleOwnership is null)
			{
				liveClaims.Add(claim);
			}
			else
			{
				claimStore.DeleteClaim(claim.OwnerId);
			}
		}

		return new ExplorerRegistrationSelection(
			liveClaims.FirstOrDefault(claim => claim.SingleEnabled),
			liveClaims.FirstOrDefault(claim => claim.PairEnabled),
			liveClaims.Count > 1);
	}

	private string GetOwnerPath(string ownerId) => Path.Combine(rootDirectory, ownerId + ".owner");
}
