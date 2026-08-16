using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Projections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies bounded late legacy enrichment over progressive base state.</summary>
[TestClass]
public sealed class Win32LegacyItemEnrichmentAdapterTests
{
	/// <summary>Ensures blocking legacy work does not delay the first state.</summary>
	[TestMethod]
	public async Task Enrichment_DoesNotBlockFirstStatePublication()
	{
		var materializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseMaterializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var item = CreateItem("first.txt");
		var projection = FolderItemListedItemProjectionTestFactory.Create();
		await using var enrichment = new Win32LegacyItemEnrichmentAdapter(
			Task.FromResult(false),
			projection,
			async (sourceItem, cancellationToken) =>
			{
				materializerStarted.TrySetResult(true);
				await releaseMaterializer.Task.WaitAsync(cancellationToken);
				return CreateResult(sourceItem);
			},
			(_, _, _, _) => Task.FromResult(true));
		await using var coordinator = new FolderPublicationCoordinator(enrichment);
		var states = new List<FolderPublicationState>();
		var readTask = ReadStatesAsync(coordinator, states);

		var enumerateTask = coordinator.EnumerateAsync(
			SingleBatchAsync(item),
			CancellationToken.None);

		await materializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForCountAsync(states, 1);
		Assert.IsFalse(states[0].IsFinal);

		releaseMaterializer.TrySetResult(true);
		await enumerateTask;
		await readTask;
	}

	/// <summary>Ensures enrichment concurrency never exceeds the configured worker bound.</summary>
	[TestMethod]
	public async Task Enrichment_UsesBoundedWorkerConcurrency()
	{
		var activeWorkers = 0;
		var maximumWorkers = 0;
		var appliedItems = 0;
		await using var enrichment = new Win32LegacyItemEnrichmentAdapter(
			Task.FromResult(false),
			FolderItemListedItemProjectionTestFactory.Create(),
			async (item, cancellationToken) =>
			{
				var active = Interlocked.Increment(ref activeWorkers);
				while (true)
				{
					var observed = Volatile.Read(ref maximumWorkers);
					if (active <= observed || Interlocked.CompareExchange(ref maximumWorkers, active, observed) == observed)
						break;
				}

				await Task.Delay(20, cancellationToken);
				Interlocked.Decrement(ref activeWorkers);
				return CreateResult(item);
			},
			(_, _, _, _) =>
			{
				Interlocked.Increment(ref appliedItems);
				return Task.FromResult(true);
			},
			queueCapacity: 4,
			workerCount: 2);

		for (var index = 0; index < 12; index++)
		{
			var item = CreateItem($"item-{index}");
			await enrichment.EnqueueAsync(item, 1, CancellationToken.None);
		}

		await enrichment.CompleteAsync(CancellationToken.None);

		Assert.IsTrue(maximumWorkers <= 2);
		Assert.AreEqual(12, appliedItems);
	}

	/// <summary>Ensures an optional materialization failure leaves the cheap item visible.</summary>
	[TestMethod]
	public async Task EnrichmentFailure_LeavesBaseProjectionVisible()
	{
		var item = CreateItem("visible.txt");
		var projection = FolderItemListedItemProjectionTestFactory.Create();
		var updateCalled = false;
		await using var enrichment = new Win32LegacyItemEnrichmentAdapter(
			Task.FromResult(false),
			projection,
			(_, _) => throw new InvalidOperationException("legacy failure"),
			(_, _, _, _) =>
			{
				updateCalled = true;
				return Task.FromResult(true);
			});

		await enrichment.EnqueueAsync(item, 1, CancellationToken.None);
		await enrichment.CompleteAsync(CancellationToken.None);

		var projected = projection.ProjectState(new FolderPublicationState(
			1,
			ImmutableArray.Create(item)));

		Assert.IsFalse(updateCalled);
		Assert.AreEqual(1, projected.Count);
		Assert.AreEqual(item.Name, projected[0].ItemNameRaw);
	}

	private static async Task ReadStatesAsync(
		FolderPublicationCoordinator coordinator,
		ICollection<FolderPublicationState> states)
	{
		await foreach (var state in coordinator.ReadStates())
			states.Add(state);
	}

	private static async Task WaitForCountAsync<T>(IReadOnlyCollection<T> items, int expectedCount)
	{
		var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (items.Count < expectedCount && DateTime.UtcNow < timeout)
			await Task.Delay(10);

		Assert.AreEqual(expectedCount, items.Count);
	}

	private static FolderItem CreateItem(string name)
		=> new(new FolderItemKey("win32", Path.Combine(Path.GetTempPath(), name)), name, FolderItemKind.File, null, null);

	private static async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> SingleBatchAsync(FolderItem item)
	{
		yield return new FolderEnumerationBatch<FolderItem>([item], 0);
		await Task.CompletedTask;
	}

	private static Win32LegacyItemEnrichmentResult CreateResult(FolderItem item)
		=> new(
			new ListedItem(null!)
			{
				ItemPath = item.Key.OpaqueId,
				ItemNameRaw = item.Name,
			},
			Array.Empty<ListedItem>());
}
