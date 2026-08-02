using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests;

[TestClass]
public sealed class EnumerationSnapshotCoalescerTests
{
	[TestMethod]
	public async Task AppliesLatestSnapshotWhenSeveralArriveBeforeDispatcherRuns()
	{
		var scheduler = new ManualScheduler();
		var applied = new List<int[]>();
		var coalescer = CreateCoalescer(scheduler, applied);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		coalescer.Submit(new[] { 1, 2 }, CancellationToken.None);
		Assert.AreEqual(1, scheduler.ScheduledCount);
		await scheduler.RunNextAsync();

		Assert.AreEqual(1, applied.Count);
		CollectionAssert.AreEqual(new[] { 1, 2 }, applied[0]);
		Assert.AreEqual(0, scheduler.ScheduledCount);
	}

	[TestMethod]
	public void SchedulesOnlyOneDispatcherCallbackForPendingSnapshots()
	{
		var scheduler = new ManualScheduler();
		var coalescer = CreateCoalescer(scheduler, new List<int[]>());

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		coalescer.Submit(new[] { 2 }, CancellationToken.None);
		coalescer.Submit(new[] { 3 }, CancellationToken.None);

		Assert.AreEqual(1, scheduler.ScheduledCount);
	}

	[TestMethod]
	public async Task CancellationSkipsPendingSnapshot()
	{
		var scheduler = new ManualScheduler();
		var applied = new List<int[]>();
		var coalescer = CreateCoalescer(scheduler, applied);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		coalescer.Cancel();
		await scheduler.RunNextAsync();

		Assert.AreEqual(0, applied.Count);
	}

	[TestMethod]
	public async Task ApplyFailureDoesNotLeaveCoalescerStuck()
	{
		var scheduler = new ManualScheduler();
		var applied = new List<int[]>();
		var failures = new List<Exception>();
		var failNext = true;
		var coalescer = new EnumerationSnapshotCoalescer<int>(
			(snapshot, _) =>
			{
				if (failNext)
				{
					failNext = false;
					throw new InvalidOperationException("test failure");
				}

				applied.Add(snapshot.ToArray());
				return Task.CompletedTask;
			},
			callback => scheduler.ScheduleAsync(callback),
			failures.Add);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		await scheduler.RunNextAsync();
		coalescer.Submit(new[] { 2 }, CancellationToken.None);
		await scheduler.RunNextAsync();

		Assert.AreEqual(1, failures.Count);
		CollectionAssert.AreEqual(new[] { 2 }, applied.Single());
	}

	private static EnumerationSnapshotCoalescer<int> CreateCoalescer(ManualScheduler scheduler, List<int[]> applied)
	{
		return new EnumerationSnapshotCoalescer<int>(
			(snapshot, _) =>
			{
				applied.Add(snapshot.ToArray());
				return Task.CompletedTask;
			},
			callback => scheduler.ScheduleAsync(callback));
	}

	private sealed class ManualScheduler
	{
		private readonly Queue<Func<Task>> callbacks = new();

		public int ScheduledCount
		{
			get
			{
				lock (callbacks)
					return callbacks.Count;
			}
		}

		public Task ScheduleAsync(Func<Task> callback)
		{
			lock (callbacks)
				callbacks.Enqueue(callback);

			return Task.CompletedTask;
		}

		public async Task RunNextAsync()
		{
			Func<Task> callback;
			lock (callbacks)
				callback = callbacks.Dequeue();

			await callback();
		}
	}
}
