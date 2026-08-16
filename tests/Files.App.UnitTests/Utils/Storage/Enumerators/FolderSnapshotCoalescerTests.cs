using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators;

/// <summary>Verifies latest-wins snapshot scheduling before the UI boundary.</summary>
[TestClass]
public sealed class FolderSnapshotCoalescerTests
{
	/// <summary>Ensures the first submitted snapshot schedules without waiting for another state.</summary>
	[TestMethod]
	public async Task Submit_SchedulesFirstSnapshotImmediately()
	{
		var applied = new List<string>();
		var coalescer = new FolderSnapshotCoalescer<string>(
			new ImmediateSnapshotScheduler(),
			snapshot =>
			{
				applied.Add(snapshot.Single());
				return Task.CompletedTask;
			});

		coalescer.Submit(["first"], CancellationToken.None);
		await WaitForCountAsync(applied, 1);

		CollectionAssert.AreEqual(new[] { "first" }, applied);
		await coalescer.CancelAsync();
	}

	/// <summary>Ensures only the newest pending snapshot remains behind one in-flight apply.</summary>
	[TestMethod]
	public async Task Submit_KeepsOneInFlightAndLatestPendingSnapshot()
	{
		var scheduler = new ManualSnapshotScheduler();
		var applied = new List<string>();
		await using var coalescer = new FolderSnapshotCoalescer<string>(
			scheduler,
			snapshot =>
			{
				applied.Add(snapshot.Single());
				return Task.CompletedTask;
			});

		coalescer.Submit(["first"], CancellationToken.None);
		await scheduler.WaitForScheduledCountAsync(1);
		coalescer.Submit(["second"], CancellationToken.None);
		coalescer.Submit(["latest"], CancellationToken.None);

		Assert.AreEqual(1, scheduler.ScheduledCount);
		scheduler.ReleaseNext();
		await scheduler.WaitForScheduledCountAsync(2);
		scheduler.ReleaseNext();
		await WaitForCountAsync(applied, 2);

		CollectionAssert.AreEqual(new[] { "first", "latest" }, applied);
	}

	/// <summary>Ensures a final snapshot replaces pending intermediates and drains last.</summary>
	[TestMethod]
	public async Task SubmitFinal_ReplacesPendingSnapshotAndDrainDoesNotLoseFinal()
	{
		var scheduler = new ManualSnapshotScheduler();
		var applied = new List<string>();
		await using var coalescer = new FolderSnapshotCoalescer<string>(
			scheduler,
			snapshot =>
			{
				applied.Add(snapshot.Single());
				return Task.CompletedTask;
			});

		coalescer.Submit(["first"], CancellationToken.None);
		await scheduler.WaitForScheduledCountAsync(1);
		coalescer.Submit(["intermediate"], CancellationToken.None);
		coalescer.SubmitFinal(["final"], CancellationToken.None);

		scheduler.ReleaseNext();
		await scheduler.WaitForScheduledCountAsync(2);
		scheduler.ReleaseNext();
		await coalescer.DrainAsync(CancellationToken.None);

		CollectionAssert.AreEqual(new[] { "first", "final" }, applied);
	}

	/// <summary>Ensures canceled or stale generations never schedule a snapshot.</summary>
	[TestMethod]
	public async Task Submit_RejectsCanceledAndStaleGenerations()
	{
		var scheduler = new ImmediateSnapshotScheduler();
		var applied = new List<string>();
		var isCurrent = false;
		await using var coalescer = new FolderSnapshotCoalescer<string>(
			scheduler,
			snapshot =>
			{
				applied.Add(snapshot.Single());
				return Task.CompletedTask;
			},
			() => isCurrent);
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		coalescer.Submit(["canceled"], cancellationTokenSource.Token);
		coalescer.SubmitFinal(["stale"], CancellationToken.None);
		await Task.Delay(50);

		Assert.AreEqual(0, applied.Count);
	}

	private static async Task WaitForCountAsync<T>(IReadOnlyCollection<T> items, int expectedCount)
	{
		var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (items.Count < expectedCount && DateTime.UtcNow < timeout)
			await Task.Delay(10);

		Assert.AreEqual(expectedCount, items.Count);
	}

	private sealed class ImmediateSnapshotScheduler : IFolderSnapshotScheduler
	{
		public Task ScheduleAsync(Func<Task> callback)
			=> callback();
	}

	private sealed class ManualSnapshotScheduler : IFolderSnapshotScheduler
	{
		private readonly object syncRoot = new();
		private readonly Queue<(Func<Task> Callback, TaskCompletionSource<bool> Release)> callbacks = new();
		private TaskCompletionSource<bool>? scheduleChanged;

		public int ScheduledCount { get; private set; }

		public Task ScheduleAsync(Func<Task> callback)
		{
			ArgumentNullException.ThrowIfNull(callback);
			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			lock (syncRoot)
			{
				callbacks.Enqueue((callback, release));
				ScheduledCount++;
				scheduleChanged?.TrySetResult(true);
				scheduleChanged = null;
			}

			return RunWhenReleasedAsync(callback, release);
		}

		public async Task WaitForScheduledCountAsync(int expectedCount)
		{
			while (true)
			{
				Task waitTask;
				lock (syncRoot)
				{
					if (ScheduledCount >= expectedCount)
						return;

					scheduleChanged ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					waitTask = scheduleChanged.Task;
				}

				await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
		}

		public void ReleaseNext()
		{
			lock (syncRoot)
			{
				Assert.IsTrue(callbacks.Count > 0);
				var next = callbacks.Dequeue();
				next.Release.TrySetResult(true);
			}
		}

		private static async Task RunWhenReleasedAsync(
			Func<Task> callback,
			TaskCompletionSource<bool> release)
		{
			await release.Task;
			await callback();
		}
	}
}
