using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Contracts;
using Files.App.Services;
using Files.App.Utils;
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

	private static ListedItem CreateItem()
		=> (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));

	private sealed class BlockingIconCacheService : IIconCacheService
	{
		private readonly TaskCompletionSource<byte[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int active;
		private int maxConcurrent;

		public TaskCompletionSource<bool> ReachedFourWorkers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

		public async Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
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
			}
		}

		public void Clear()
		{
		}

		public void Complete() => completion.TrySetResult(new byte[] { 1 });
	}
}
