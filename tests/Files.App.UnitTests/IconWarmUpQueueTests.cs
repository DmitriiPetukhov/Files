using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Contracts;
using Files.App.Services;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class IconWarmUpQueueTests
{
	[TestMethod]
	public async Task TryQueue_DropsOptionalRequestsWhenCapacityIsFull()
	{
		var cache = new BlockingIconCacheService();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 2, workerCount: 1);
		var item = CreateItem();

		for (var i = 0; i < 10; i++)
			queue.TryQueue(item, false, CancellationToken.None);

		Assert.IsTrue(queue.DroppedCount > 0);
		cache.Complete();
	}

	[TestMethod]
	public async Task Workers_DoNotExceedConfiguredConcurrency()
	{
		var cache = new BlockingIconCacheService();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 32, workerCount: 4);
		var item = CreateItem();

		for (var i = 0; i < 8; i++)
			queue.TryQueue(item, false, CancellationToken.None);

		await cache.ReachedFourWorkers.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(4, cache.MaxConcurrent);
		cache.Complete();
	}

	[TestMethod]
	public async Task TryQueue_PreservesEnumerationKindForShortcutAndFolder()
	{
		var cache = new RecordingIconCacheService();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 4, workerCount: 1);

		UniversalStorageEnumerator.QueueIconWarmUp(queue, CreateItem(), isFolderFromEnumeration: false, CancellationToken.None);
		UniversalStorageEnumerator.QueueIconWarmUp(queue, CreateItem(), isFolderFromEnumeration: true, CancellationToken.None);

		await cache.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(2, cache.IsFolderArguments.Count);
		Assert.IsFalse(cache.IsFolderArguments[0]);
		Assert.IsTrue(cache.IsFolderArguments[1]);
	}

	[TestMethod]
	public async Task UniversalEnumeratorBoundary_QueuesWithoutWaitingForIcon()
	{
		var cache = new BlockingIconCacheService();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 1, workerCount: 1);

		UniversalStorageEnumerator.QueueIconWarmUp(queue, CreateItem(), false, CancellationToken.None);

		await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cache.Complete();
		await cache.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[TestMethod]
	public async Task WorkerContainsCacheFailure()
	{
		var cache = new FailingIconCacheService();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 1, workerCount: 1);

		queue.TryQueue(CreateItem(), false, CancellationToken.None);

		await cache.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => queue.FailureCount == 1);
	}

	[TestMethod]
	public async Task CancellationAfterLoadSkipsPublication()
	{
		var cache = new BlockingIconCacheService();
		using var cancellation = new CancellationTokenSource();
		await using var queue = new IconWarmUpQueue(cache, NullLogger<IconWarmUpQueue>.Instance, capacity: 1, workerCount: 1);
		var item = CreateItem();

		queue.TryQueue(item, false, cancellation.Token);
		await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();
		cache.Complete();

		await cache.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitUntilAsync(() => queue.StaleSkippedCount == 1);
		Assert.IsNull(item.PreloadedIconData);
	}

	private static async Task WaitUntilAsync(Func<bool> predicate)
	{
		for (var attempt = 0; attempt < 50 && !predicate(); attempt++)
			await Task.Delay(10);

		Assert.IsTrue(predicate());
	}

	private static ListedItem CreateItem()
		=> (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));

	private sealed class BlockingIconCacheService : IIconCacheService
	{
		private readonly TaskCompletionSource<byte[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int active;
		private int maxConcurrent;

		public TaskCompletionSource<bool> ReachedFourWorkers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

		public async Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
			Started.TrySetResult(true);
			var current = Interlocked.Increment(ref active);
			while (current > Volatile.Read(ref maxConcurrent))
				Interlocked.CompareExchange(ref maxConcurrent, current, Volatile.Read(ref maxConcurrent));

			if (current >= 4)
				ReachedFourWorkers.TrySetResult(true);

			try
			{
				return await completion.Task;
			}
			finally
			{
				Interlocked.Decrement(ref active);
				Returned.TrySetResult(true);
			}
		}

		public void Clear()
		{
		}

		public void Complete() => completion.TrySetResult(new byte[] { 1 });
	}

	private sealed class RecordingIconCacheService : IIconCacheService
	{
		public List<bool> IsFolderArguments { get; } = new();
		public TaskCompletionSource<bool> Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
			lock (IsFolderArguments)
			{
				IsFolderArguments.Add(isFolder);
				if (IsFolderArguments.Count == 2)
					Processed.TrySetResult(true);
			}

			return Task.FromResult<byte[]?>(Array.Empty<byte>());
		}

		public void Clear()
		{
		}
	}

	private sealed class FailingIconCacheService : IIconCacheService
	{
		public TaskCompletionSource<bool> FailureObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
			FailureObserved.TrySetResult(true);
			return Task.FromException<byte[]?>(new InvalidOperationException("test failure"));
		}

		public void Clear()
		{
		}
	}
}
