using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.ExternalDiff;
using ClipDiff.Windows.Ownership;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerRegistrationCoordinatorTests
{
	[TestMethod]
	public void StartingAndQuittingSecondOwnerPreservesFirstOwnersRegistration()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var first = new ExplorerRegistrationCoordinator(store, directory);
			using var second = new ExplorerRegistrationCoordinator(store, directory);
			ExplorerRegistrationSelection? selection = null;

			bool Apply(ExplorerRegistrationSelection value)
			{
				selection = value;
				return true;
			}

			first.Update(CreateClaim("first", enabled: true), Apply);
			second.Update(CreateClaim("second", enabled: false), Apply);

			Assert.AreEqual(first.OwnerId, selection!.Single!.OwnerId);
			Assert.AreEqual(first.OwnerId, selection.Pair!.OwnerId);
			Assert.IsTrue(selection.MultipleOwners);

			second.Remove(Apply);

			Assert.AreEqual(first.OwnerId, selection.Single!.OwnerId);
			Assert.AreEqual("first", selection.Single.DisplayName);
			Assert.AreEqual(first.OwnerId, selection.Pair!.OwnerId);
			Assert.IsFalse(selection.MultipleOwners);

			first.Remove(Apply);

			Assert.IsNull(selection.Single);
			Assert.IsNull(selection.Pair);
			Assert.AreEqual(0, store.ReadClaims().Count);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public void QuittingSelectedOwnerRestoresSurvivingOwnersCommandsAndLabels()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var first = new ExplorerRegistrationCoordinator(store, directory);
			using var second = new ExplorerRegistrationCoordinator(store, directory);
			ExplorerRegistrationSelection? selection = null;

			bool Apply(ExplorerRegistrationSelection value)
			{
				selection = value;
				return true;
			}

			first.Update(CreateClaim("first", enabled: true), Apply);
			second.Update(CreateClaim("second", enabled: true), Apply);
			var selected = selection!.Single!.OwnerId == first.OwnerId ? first : second;
			var survivor = ReferenceEquals(selected, first) ? second : first;
			var survivorLabel = ReferenceEquals(survivor, first) ? "first" : "second";

			selected.Remove(Apply);

			Assert.AreEqual(survivor.OwnerId, selection.Single!.OwnerId);
			Assert.AreEqual(survivorLabel, selection.Single.DisplayName);
			Assert.AreEqual(survivorLabel + " single", selection.Single.SingleCommandLine);
			Assert.AreEqual(survivor.OwnerId, selection.Pair!.OwnerId);
			Assert.AreEqual(survivorLabel + " pair", selection.Pair.ComServerCommandLine);
			Assert.IsFalse(selection.MultipleOwners);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public void StaleOwnerAfterAbnormalExitIsRemovedWithoutDiscardingLiveOwner()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var stale = new ExplorerRegistrationCoordinator(store, directory);
			using var live = new ExplorerRegistrationCoordinator(store, directory);
			stale.Update(CreateClaim("stale", enabled: true), _ => true);
			stale.Dispose();
			ExplorerRegistrationSelection? selection = null;

			live.Update(CreateClaim("live", enabled: true), value =>
			{
				selection = value;
				return true;
			});

			Assert.AreEqual(live.OwnerId, selection!.Single!.OwnerId);
			Assert.AreEqual(live.OwnerId, selection.Pair!.OwnerId);
			Assert.AreEqual(1, store.ReadClaims().Count);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public async Task UpdateWaitsForConcurrentOwnersGateRatherThanLosingState()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var owner = new ExplorerRegistrationCoordinator(store, directory);
			using var gate = SharedFileLease.Acquire(Path.Combine(directory, "coordination.lock"));
			var update = Task.Run(() => owner.Update(CreateClaim("owner", enabled: true), _ => true));
			await Task.Delay(50);

			Assert.IsFalse(update.IsCompleted);
			gate.Dispose();

			Assert.IsTrue(await update);
			Assert.AreEqual(owner.OwnerId, store.ReadClaims().Single().OwnerId);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public async Task QuitWaitsForConcurrentOwnersGateAndRestoresSurvivor()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var survivor = new ExplorerRegistrationCoordinator(store, directory);
			using var quitting = new ExplorerRegistrationCoordinator(store, directory);
			survivor.Update(CreateClaim("survivor", enabled: true), _ => true);
			quitting.Update(CreateClaim("quitting", enabled: true), _ => true);
			using var gate = SharedFileLease.Acquire(Path.Combine(directory, "coordination.lock"));
			ExplorerRegistrationSelection? selection = null;
			var removal = Task.Run(() => quitting.Remove(value =>
			{
				selection = value;
				return true;
			}));
			await Task.Delay(50);

			Assert.IsFalse(removal.IsCompleted);
			gate.Dispose();

			Assert.IsTrue(await removal);
			Assert.AreEqual(survivor.OwnerId, selection!.Single!.OwnerId);
			Assert.AreEqual(survivor.OwnerId, selection.Pair!.OwnerId);
			Assert.AreEqual(1, store.ReadClaims().Count);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public void RefreshReconcilesStateAfterBusyGateExceedsDeadline()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var owner = new ExplorerRegistrationCoordinator(store, directory);
			using var gate = SharedFileLease.Acquire(Path.Combine(directory, "coordination.lock"));

			Assert.IsFalse(owner.Update(CreateClaim("owner", enabled: true), _ => true));
			Assert.AreEqual(0, store.ReadClaims().Count);
			gate.Dispose();

			Assert.IsTrue(owner.Update(CreateClaim("owner", enabled: true), _ => true));
			Assert.AreEqual(owner.OwnerId, store.ReadClaims().Single().OwnerId);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	[TestMethod]
	public void SurvivingOwnerRefreshRemovesQuitClaimWhenCleanupGateTimedOut()
	{
		var directory = CreateTemporaryDirectory();

		try
		{
			var store = new MemoryClaimStore();
			using var survivor = new ExplorerRegistrationCoordinator(store, directory);
			using var quitting = new ExplorerRegistrationCoordinator(store, directory);
			survivor.Update(CreateClaim("survivor", enabled: true), _ => true);
			quitting.Update(CreateClaim("quitting", enabled: true), _ => true);
			using var gate = SharedFileLease.Acquire(Path.Combine(directory, "coordination.lock"));

			Assert.IsFalse(quitting.Remove(_ => true));
			gate.Dispose();
			ExplorerRegistrationSelection? selection = null;

			survivor.Update(CreateClaim("survivor", enabled: true), value =>
			{
				selection = value;
				return true;
			});

			Assert.AreEqual(survivor.OwnerId, selection!.Single!.OwnerId);
			Assert.AreEqual(survivor.OwnerId, selection.Pair!.OwnerId);
			Assert.AreEqual(1, store.ReadClaims().Count);
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(directory);
		}
	}

	private static ExplorerRegistrationClaim CreateClaim(string label, bool enabled) =>
		new(string.Empty, enabled, label, label + " single", label + ".exe", enabled, label + " pair", label + ".dll");

	private static string CreateTemporaryDirectory() =>
		Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));

	private sealed class MemoryClaimStore : IExplorerRegistrationClaimStore
	{
		private readonly Dictionary<string, ExplorerRegistrationClaim> claims = [];

		public IReadOnlyList<ExplorerRegistrationClaim> ReadClaims() => [.. claims.Values];

		public void WriteClaim(ExplorerRegistrationClaim claim) => claims[claim.OwnerId] = claim;

		public void DeleteClaim(string ownerId) => claims.Remove(ownerId);
	}
}
