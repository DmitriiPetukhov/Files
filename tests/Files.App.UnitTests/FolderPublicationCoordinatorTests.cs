using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationCoordinatorTests
{
	[TestMethod]
	public async Task EnumerateAsync_PublishesAccumulatedBatchesAndAuthoritativeFinalResult()
	{
		var snapshots = new List<IReadOnlyCollection<string>>();
		var coordinator = new FolderPublicationCoordinator<string>(
			StringComparer.Ordinal,
			snapshot =>
			{
				snapshots.Add(snapshot.ToArray());
				return Task.CompletedTask;
			});

		var finalItems = await coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<string>(
			[
				(IReadOnlyCollection<string>)["b", "a"],
				(IReadOnlyCollection<string>)["d", "c"]
			],
			["d", "c", "b", "a"]),
			CancellationToken.None);

		Assert.AreEqual(3, snapshots.Count);
		CollectionAssert.AreEqual(new[] { "a", "b" }, snapshots[0].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, snapshots[1].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, snapshots[2].ToArray());
		CollectionAssert.AreEqual(new[] { "d", "c", "b", "a" }, finalItems.ToArray());
	}

	[TestMethod]
	public async Task TryRebuildIndexAsync_PublishesNewSortOrder()
	{
		var snapshots = new List<IReadOnlyCollection<string>>();
		var coordinator = new FolderPublicationCoordinator<string>(
			StringComparer.Ordinal,
			snapshot =>
			{
				snapshots.Add(snapshot.ToArray());
				return Task.CompletedTask;
			});

		await coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<string>(
				[(IReadOnlyCollection<string>)["b", "a"]],
				["b", "a"]),
				CancellationToken.None);

		var rebuilt = await coordinator.TryRebuildIndexAsync(
			Comparer<string>.Create((left, right) => string.CompareOrdinal(right, left)),
			CancellationToken.None);

		Assert.IsTrue(rebuilt);
		CollectionAssert.AreEqual(new[] { "b", "a" }, snapshots[^1].ToArray());
	}

	[TestMethod]
	public async Task TryRebuildIndexAsync_SerializesWithEnumerationPublication()
	{
		var firstPublicationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstPublication = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var snapshots = new List<IReadOnlyCollection<string>>();
		var publicationCount = 0;
		var coordinator = new FolderPublicationCoordinator<string>(
			StringComparer.Ordinal,
			async snapshot =>
			{
				if (Interlocked.Increment(ref publicationCount) == 1)
				{
					firstPublicationStarted.SetResult(true);
					await releaseFirstPublication.Task;
				}

				snapshots.Add(snapshot.ToArray());
			});

		var enumerationTask = coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<string>(
				[(IReadOnlyCollection<string>)["b", "a"]],
				["b", "a"]),
				CancellationToken.None);

		await firstPublicationStarted.Task;
		var rebuildTask = coordinator.TryRebuildIndexAsync(
			Comparer<string>.Create((left, right) => string.CompareOrdinal(right, left)),
			CancellationToken.None);

		releaseFirstPublication.SetResult(true);
		await enumerationTask;

		Assert.IsTrue(await rebuildTask);
		CollectionAssert.AreEqual(new[] { "b", "a" }, snapshots[^1].ToArray());
	}

	[TestMethod]
	public async Task TryRebuildIndexAsync_RunsWithoutBlockingCaller()
	{
		var coordinator = new FolderPublicationCoordinator<int>(
			Comparer<int>.Default,
			_ => Task.CompletedTask);

		await coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<int>(
				[(IReadOnlyCollection<int>)[1, 2, 3]],
				[1, 2, 3]),
				CancellationToken.None);

		using var comparerEntered = new ManualResetEventSlim();
		using var releaseComparer = new ManualResetEventSlim();
		var comparer = new BlockingComparer(comparerEntered, releaseComparer);

		try
		{
			var rebuildTask = coordinator.TryRebuildIndexAsync(comparer, CancellationToken.None);

			Assert.IsTrue(comparerEntered.Wait(TimeSpan.FromSeconds(5)));
			Assert.IsFalse(rebuildTask.IsCompleted);

			releaseComparer.Set();
			Assert.IsTrue(await rebuildTask);
		}
		finally
		{
			releaseComparer.Set();
		}
	}

	[TestMethod]
	public async Task CancelAsync_IsIdempotentAndRejectsLateSourceCallbacks()
	{
		var snapshots = new List<IReadOnlyCollection<string>>();
		var coordinator = new FolderPublicationCoordinator<string>(
			StringComparer.Ordinal,
			snapshot =>
			{
				snapshots.Add(snapshot.ToArray());
				return Task.CompletedTask;
			});

		await coordinator.CancelAsync();
		await coordinator.CancelAsync();

		await coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<string>(
				[(IReadOnlyCollection<string>)["a"]],
				["a"]),
				CancellationToken.None);

		Assert.AreEqual(0, snapshots.Count);
	}

	private sealed class FakeFolderEnumerationSource<T>(
		IReadOnlyList<IReadOnlyCollection<T>> batches,
		IReadOnlyCollection<T> finalItems) : IFolderEnumerationSource<T>
	{
		public async Task<IReadOnlyCollection<T>> EnumerateAsync(
			Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
			CancellationToken cancellationToken)
		{
			foreach (var batch in batches)
				await publishBatchAsync(batch);

			return finalItems;
		}
	}

	private sealed class BlockingComparer(ManualResetEventSlim entered, ManualResetEventSlim release) : Comparer<int>
	{
		public override int Compare(int x, int y)
		{
			entered.Set();
			release.Wait();
			return y.CompareTo(x);
		}
	}
}
