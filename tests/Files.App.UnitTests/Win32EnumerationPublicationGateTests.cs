using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32EnumerationPublicationGateTests
{
	[TestMethod]
	public async Task PublishesFirstBatchWhenEightItemsAreReady()
	{
		var published = new List<int[]>();
		var gate = CreateGate(published);

		for (var item = 1; item <= 8; item++)
			await gate.AddAsync(item, CancellationToken.None);

		Assert.AreEqual(1, published.Count);
		CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), published[0]);
	}

	[TestMethod]
	public async Task PublishesNonEmptyPartialBatchWhenTimerExpires()
	{
		var published = new List<int[]>();
		var delay = new ControlledDelay();
		var gate = CreateGate(published, delay.DelayAsync);

		await gate.AddAsync(1, CancellationToken.None);
		await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		delay.Release();
		await WaitUntilAsync(() => published.Count == 1);

		CollectionAssert.AreEqual(new[] { 1 }, published[0]);
	}

	[TestMethod]
	public async Task DoesNotStartTimerForEmptyBatch()
	{
		var published = new List<int[]>();
		var delay = new ControlledDelay();
		var gate = CreateGate(published, delay.DelayAsync);

		await gate.FlushAsync(CancellationToken.None);

		Assert.IsFalse(delay.Started.Task.IsCompleted);
		Assert.AreEqual(0, published.Count);
	}

	[TestMethod]
	public async Task FlushPublishesRemainingItemsImmediately()
	{
		var published = new List<int[]>();
		var gate = CreateGate(published);

		await gate.AddAsync(1, CancellationToken.None);
		await gate.AddAsync(2, CancellationToken.None);
		await gate.FlushAsync(CancellationToken.None);

		Assert.AreEqual(1, published.Count);
		CollectionAssert.AreEqual(new[] { 1, 2 }, published[0]);
	}

	[TestMethod]
	public async Task CancelDropsPendingItemsAndTimer()
	{
		var published = new List<int[]>();
		var delay = new ControlledDelay();
		var gate = CreateGate(published, delay.DelayAsync);

		await gate.AddAsync(1, CancellationToken.None);
		await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await gate.CancelAsync();
		delay.Release();
		await Task.Delay(50);

		Assert.AreEqual(0, published.Count);
	}

	[TestMethod]
	public async Task TimerAndCountFlushesAreSerialized()
	{
		var published = new List<int[]>();
		var firstPublicationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstPublication = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var delay = new ControlledDelay();
		var gate = new Win32EnumerationPublicationGate<int>(
			async batch =>
			{
				lock (published)
					published.Add(batch.ToArray());

				if (batch[0] == 1)
				{
					firstPublicationStarted.TrySetResult(true);
					await releaseFirstPublication.Task;
				}
			},
			initialBatchSize: 8,
			intermediateBatchSize: 32,
			batchTimeout: TimeSpan.FromMilliseconds(500),
			delay.DelayAsync);

		for (var item = 1; item <= 7; item++)
			await gate.AddAsync(item, CancellationToken.None);

		var firstFlush = gate.AddAsync(8, CancellationToken.None);
		await firstPublicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var secondItem = gate.AddAsync(9, CancellationToken.None);
		await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		delay.Release();
		releaseFirstPublication.TrySetResult(true);

		await firstFlush;
		await secondItem;
		await WaitUntilAsync(() => published.Count == 2);

		CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), published[0]);
		CollectionAssert.AreEqual(new[] { 9 }, published[1]);
	}

	private static Win32EnumerationPublicationGate<int> CreateGate(
		List<int[]> published,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
	{
		return new Win32EnumerationPublicationGate<int>(
			batch =>
			{
				lock (published)
					published.Add(batch.ToArray());

				return Task.CompletedTask;
			},
			initialBatchSize: 8,
			intermediateBatchSize: 32,
			batchTimeout: TimeSpan.FromMilliseconds(500),
			delayAsync);
	}

	private static async Task WaitUntilAsync(Func<bool> predicate)
	{
		for (var attempt = 0; attempt < 50 && !predicate(); attempt++)
			await Task.Delay(10);

		Assert.IsTrue(predicate());
	}

	private sealed class ControlledDelay
	{
		private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task DelayAsync(TimeSpan _, CancellationToken cancellationToken)
		{
			Started.TrySetResult(true);
			await release.Task.WaitAsync(cancellationToken);
		}

		public void Release() => release.TrySetResult(true);
	}
}
