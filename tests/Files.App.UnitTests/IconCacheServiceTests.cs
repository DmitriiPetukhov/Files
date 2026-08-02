using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class IconCacheServiceTests
{
	[TestMethod]
	public async Task GetIconAsync_ConcurrentRequestsShareOneLoaderTask()
	{
		var loader = new TestIconLoader();
		var cache = new IconCacheService(loader);
		var requests = Enumerable.Range(0, 8)
			.Select(_ => cache.GetIconAsync("C:\\item.cs", ".cs", false))
			.ToArray();

		await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(1, loader.CallCount);

		loader.Complete(new byte[] { 1, 2, 3 });
		var results = await Task.WhenAll(requests);

		Assert.AreEqual(8, results.Length);
		foreach (var result in results)
			CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result);
	}

	private sealed class TestIconLoader : IIconLoader
	{
		private readonly TaskCompletionSource<byte[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int callCount;

		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int CallCount => Volatile.Read(ref callCount);

		public Task<byte[]?> LoadAsync(string iconPath, bool isFolder)
		{
			Interlocked.Increment(ref callCount);
			Started.TrySetResult(true);
			return completion.Task;
		}

		public void Complete(byte[] value) => completion.TrySetResult(value);
	}
}
