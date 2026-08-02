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
	public async Task CanceledSnapshotTokenSkipsApplyWithoutReportingFailure()
	{
		var scheduler = new ManualScheduler();
		var applied = new List<int[]>();
		var failures = new List<Exception>();
		using var cancellationSource = new CancellationTokenSource();
		var coalescer = CreateCoalescer(scheduler, applied, failures.Add);

		coalescer.Submit(new[] { 1 }, cancellationSource.Token);
		cancellationSource.Cancel();
		await scheduler.RunNextAsync();

		Assert.AreEqual(0, applied.Count);
		Assert.AreEqual(0, failures.Count);
	}

	[TestMethod]
	public async Task SchedulerFailureCompletesDrainAndAllowsLaterSubmissionToRetry()
	{
		var scheduler = new RejectingScheduler();
		var applied = new List<int[]>();
		var failures = new List<Exception>();
		var coalescer = CreateCoalescer(scheduler, applied, failures.Add);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		await coalescer.DrainAsync(CancellationToken.None);

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(0, applied.Count);

		coalescer.Submit(new[] { 2 }, CancellationToken.None);
		await scheduler.RunNextAsync();

		CollectionAssert.AreEqual(new[] { 2 }, applied.Single());
	}

	[TestMethod]
	public async Task FinalDrainRetriesApplyFailureOnce()
	{
		var scheduler = new ImmediateScheduler();
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
			scheduler,
			failures.Add);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		await coalescer.DrainAsync(CancellationToken.None, retryPendingSnapshot: true);

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(2, scheduler.ScheduleCount);
		CollectionAssert.AreEqual(new[] { 1 }, applied.Single());
	}

	[TestMethod]
	public async Task CancellationCompletesDrainWithoutApplyingSnapshot()
	{
		var scheduler = new ManualScheduler();
		var applied = new List<int[]>();
		var coalescer = CreateCoalescer(scheduler, applied);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		var drain = coalescer.DrainAsync(CancellationToken.None);
		coalescer.Cancel();

		await drain;
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
			scheduler,
			failures.Add);

		coalescer.Submit(new[] { 1 }, CancellationToken.None);
		await scheduler.RunNextAsync();
		coalescer.Submit(new[] { 2 }, CancellationToken.None);
		await scheduler.RunNextAsync();

		Assert.AreEqual(1, failures.Count);
		CollectionAssert.AreEqual(new[] { 2 }, applied.Single());
	}

	private static EnumerationSnapshotCoalescer<int> CreateCoalescer(
		IFolderSnapshotScheduler scheduler,
		List<int[]> applied,
		Action<Exception>? errorHandler = null)
	{
		return new EnumerationSnapshotCoalescer<int>(
			(snapshot, _) =>
			{
				applied.Add(snapshot.ToArray());
				return Task.CompletedTask;
			},
			scheduler,
			errorHandler);
	}

	private sealed class ManualScheduler : IFolderSnapshotScheduler
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

	private sealed class RejectingScheduler : IFolderSnapshotScheduler
	{
		private readonly ManualScheduler fallback = new();
		private bool hasRejected;

		public Task ScheduleAsync(Func<Task> callback)
		{
			if (!hasRejected)
			{
				hasRejected = true;
				throw new InvalidOperationException("test scheduler failure");
			}

			return fallback.ScheduleAsync(callback);
		}

		public Task RunNextAsync() => fallback.RunNextAsync();
	}

	private sealed class ImmediateScheduler : IFolderSnapshotScheduler
	{
		public int ScheduleCount { get; private set; }

		public Task ScheduleAsync(Func<Task> callback)
		{
			ScheduleCount++;
			return callback();
		}
	}
}
