using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationCoordinatorTests
{
	[TestMethod]
	public async Task SourceBatchesArePublishedAndFinalSnapshotIsSorted()
	{
		var applied = new List<int[]>();
		var coordinator = CreateCoordinator(applied);

		var finalSnapshot = await coordinator.EnumerateAsync(
			new TestSource(new[] { new[] { 3, 1 }, new[] { 2 } }, new[] { 3, 1, 2 }),
			CancellationToken.None);

		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, finalSnapshot.ToArray());
		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, applied.Last());
	}

	[TestMethod]
	public async Task FinalSnapshotBypassesIntermediateCooldown()
	{
		var applied = new List<int[]>();
		var delay = new ControlledDelay();
		var coordinator = new FolderPublicationCoordinator<int>(
			Comparer<int>.Default,
			(snapshot, _) =>
			{
				applied.Add(snapshot.ToArray());
				return Task.CompletedTask;
			},
			new ImmediateScheduler(),
			intermediateApplyCooldown: TimeSpan.FromMilliseconds(100),
			delayAsync: delay.DelayAsync);

		coordinator.TryPublishBatch(new[] { 1 }, CancellationToken.None);
		await coordinator.DrainAsync(CancellationToken.None);

		coordinator.TryPublishBatch(new[] { 2 }, CancellationToken.None);
		var drain = coordinator.DrainAsync(CancellationToken.None);
		await delay.Started.Task;

		Assert.AreEqual(1, applied.Count);
		Assert.IsTrue(coordinator.TryPublishFinal(new[] { 1, 2 }, (IComparer<int>?)null, CancellationToken.None, out var finalSnapshot));
		await drain;

		CollectionAssert.AreEqual(new[] { 1, 2 }, finalSnapshot!.ToArray());
		CollectionAssert.AreEqual(new[] { 1, 2 }, applied.Last());
	}

	[TestMethod]
	public async Task SourceFailureDoesNotEscapeCoordinatorCleanup()
	{
		var applied = new List<int[]>();
		var coordinator = CreateCoordinator(applied);

		try
		{
			await coordinator.EnumerateAsync(new FailingSource(), CancellationToken.None);
			Assert.Fail("The source failure should be propagated to the caller.");
		}
		catch (InvalidOperationException)
		{
		}

		await coordinator.CancelAsync();
	}

	[TestMethod]
	public async Task FinalListAdapterPreservesLegacySourceBehavior()
	{
		var publishedBatch = false;
		var expected = new[] { 3, 1, 2 };
		var source = new FinalListFolderEnumerationSource<int>(_ => Task.FromResult<IReadOnlyCollection<int>>(expected));

		var actual = await source.EnumerateAsync(
			_ =>
			{
				publishedBatch = true;
				return Task.CompletedTask;
			},
			CancellationToken.None);

		CollectionAssert.AreEqual(expected, actual.ToArray());
		Assert.IsFalse(publishedBatch);
	}

	private static FolderPublicationCoordinator<int> CreateCoordinator(List<int[]> applied)
		=> new(
			Comparer<int>.Default,
			(snapshot, _) =>
			{
				applied.Add(snapshot.ToArray());
				return Task.CompletedTask;
			},
			new ImmediateScheduler(),
			intermediateApplyCooldown: TimeSpan.Zero);

	private sealed class TestSource : IFolderEnumerationSource<int>
	{
		private readonly IReadOnlyList<IReadOnlyCollection<int>> batches;
		private readonly IReadOnlyCollection<int> finalItems;

		public TestSource(IReadOnlyList<int[]> batches, IReadOnlyCollection<int> finalItems)
		{
			this.batches = batches;
			this.finalItems = finalItems;
		}

		public async Task<IReadOnlyCollection<int>> EnumerateAsync(
			Func<IReadOnlyCollection<int>, Task> publishBatchAsync,
			CancellationToken cancellationToken)
		{
			foreach (var batch in batches)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await publishBatchAsync(batch);
			}

			return finalItems;
		}
	}

	private sealed class FailingSource : IFolderEnumerationSource<int>
	{
		public Task<IReadOnlyCollection<int>> EnumerateAsync(
			Func<IReadOnlyCollection<int>, Task> publishBatchAsync,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("test source failure");
	}

	private sealed class ImmediateScheduler : IFolderSnapshotScheduler
	{
		public Task ScheduleAsync(Func<Task> callback) => callback();
	}

	private sealed class ControlledDelay
	{
		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
		{
			Started.TrySetResult(true);
			await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
		}
	}
}
