// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage;

internal sealed class EnumerationSnapshotCoalescer<T>
{
	private readonly object syncRoot = new();
	private readonly Func<IReadOnlyCollection<T>, CancellationToken, Task> applyAsync;
	private readonly IFolderSnapshotScheduler scheduler;
	private readonly Action<Exception>? errorHandler;
	private readonly TimeSpan intermediateApplyCooldown;
	private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
	private readonly Func<DateTimeOffset> now;

	// Only the newest immutable snapshot matters. One scheduled callback will consume it
	// when the dispatcher is ready, coalescing submissions that arrive before then.
	private IReadOnlyCollection<T>? pendingSnapshot;
	private CancellationToken pendingCancellationToken;
	private bool pendingSnapshotIsFinal;
	private TaskCompletionSource<bool>? scheduledCompletion;
	private TaskCompletionSource<bool>? activeApplyCompletion;
	private CancellationTokenSource? cooldownCancellation;
	private DateTimeOffset? nextIntermediateApplyAt;
	private bool callbackScheduled;
	private bool isCanceled;

	public EnumerationSnapshotCoalescer(
		Func<IReadOnlyCollection<T>, CancellationToken, Task> applyAsync,
		IFolderSnapshotScheduler scheduler,
		Action<Exception>? errorHandler = null,
		TimeSpan intermediateApplyCooldown = default,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Func<DateTimeOffset>? now = null)
	{
		ArgumentNullException.ThrowIfNull(applyAsync);
		ArgumentNullException.ThrowIfNull(scheduler);
		if (intermediateApplyCooldown < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(intermediateApplyCooldown));

		this.applyAsync = applyAsync;
		this.scheduler = scheduler;
		this.errorHandler = errorHandler;
		this.intermediateApplyCooldown = intermediateApplyCooldown;
		this.delayAsync = delayAsync ?? Task.Delay;
		this.now = now ?? (() => DateTimeOffset.UtcNow);
	}

	public void Submit(IReadOnlyCollection<T> snapshot, CancellationToken cancellationToken)
		=> SubmitCore(snapshot, cancellationToken, isFinal: false);

	public void SubmitFinal(IReadOnlyCollection<T> snapshot, CancellationToken cancellationToken)
		=> SubmitCore(snapshot, cancellationToken, isFinal: true);

	private void SubmitCore(IReadOnlyCollection<T> snapshot, CancellationToken cancellationToken, bool isFinal)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		TaskCompletionSource<bool>? completion = null;
		CancellationTokenSource? cooldownToCancel = null;
		CancellationTokenSource? newCooldownCancellation = null;
		TimeSpan? newCooldown = null;

		lock (syncRoot)
		{
			if (isCanceled || cancellationToken.IsCancellationRequested)
				return;
			if (pendingSnapshotIsFinal && !isFinal)
				return;

			pendingSnapshot = snapshot;
			pendingCancellationToken = cancellationToken;
			pendingSnapshotIsFinal = isFinal;
			if (isFinal)
				cooldownToCancel = cooldownCancellation;

			if (!callbackScheduled)
			{
				callbackScheduled = true;
				completion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

				if (!isFinal && (newCooldown = GetRemainingCooldownLocked()) > TimeSpan.Zero)
					newCooldownCancellation = cooldownCancellation = new CancellationTokenSource();
			}
		}

		cooldownToCancel?.Cancel();

		if (completion is not null)
		{
			if (newCooldownCancellation is null)
				_ = ScheduleCallbackAsync(completion);
			else
				_ = ScheduleAfterCooldownAsync(completion, newCooldown!.Value, newCooldownCancellation);
		}
	}

	public async Task CancelAsync()
	{
		TaskCompletionSource<bool>? completion;
		CancellationTokenSource? cooldownToCancel;
		Task? activeApply;

		lock (syncRoot)
		{
			isCanceled = true;
			pendingSnapshot = null;
			pendingSnapshotIsFinal = false;
			callbackScheduled = false;
			completion = scheduledCompletion;
			scheduledCompletion = null;
			activeApply = activeApplyCompletion?.Task;
			cooldownToCancel = cooldownCancellation;
			cooldownCancellation = null;
		}

		cooldownToCancel?.Cancel();
		if (activeApply is not null)
			await activeApply.ConfigureAwait(false);

		completion?.TrySetResult(true);
	}

	public async Task DrainAsync(CancellationToken cancellationToken, bool retryPendingSnapshot = false)
	{
		var retryAttempted = false;

		while (true)
		{
			Task? completion = null;
			TaskCompletionSource<bool>? retryCompletion = null;

			lock (syncRoot)
			{
				if (callbackScheduled)
				{
					completion = scheduledCompletion?.Task;
				}
				else if (!isCanceled && retryPendingSnapshot && !retryAttempted && pendingSnapshot is not null)
				{
					// The final publication may request one bounded retry after an apply failure.
					// Intermediate failures remain observable without creating an unbounded loop.
					if (pendingCancellationToken.IsCancellationRequested)
					{
						pendingSnapshot = null;
						return;
					}

					retryAttempted = true;
					callbackScheduled = true;
					retryCompletion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					completion = retryCompletion.Task;
				}
				else
				{
					return;
				}
			}

			if (retryCompletion is not null)
				_ = ScheduleCallbackAsync(retryCompletion);

			if (completion is null)
				return;

			await completion.WaitAsync(cancellationToken);
		}
	}

	private async Task ScheduleCallbackAsync(TaskCompletionSource<bool> completion)
	{
		try
		{
			await scheduler.ScheduleAsync(() => ApplyScheduledSnapshotAsync(completion));
		}
		catch (Exception ex)
		{
			bool canceled;

			lock (syncRoot)
			{
				callbackScheduled = false;
				canceled = isCanceled;
			}

			if (!canceled)
				HandleError(ex);

			completion.TrySetResult(false);
		}
	}

	private async Task ApplyScheduledSnapshotAsync(TaskCompletionSource<bool> completion)
	{
		IReadOnlyCollection<T>? snapshot;
		CancellationToken cancellationToken;
		bool isFinal;
		TaskCompletionSource<bool>? activeApplyCompletion = null;

		lock (syncRoot)
		{
			snapshot = pendingSnapshot;
			cancellationToken = pendingCancellationToken;
			isFinal = pendingSnapshotIsFinal;
			pendingSnapshot = null;
			pendingSnapshotIsFinal = false;

			if (snapshot is not null && !cancellationToken.IsCancellationRequested && !isCanceled)
				activeApplyCompletion = this.activeApplyCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		var applyFailed = false;

		try
		{
			if (activeApplyCompletion is not null)
				await applyAsync(snapshot, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || isCanceled)
		{
		}
		catch (Exception ex)
		{
			lock (syncRoot)
			{
				applyFailed = !isCanceled;
			}

			if (applyFailed)
				HandleError(ex);
		}
		finally
		{
			TaskCompletionSource<bool>? nextCompletion = null;
			CancellationTokenSource? nextCooldownCancellation = null;
			TimeSpan? nextCooldown = null;

			lock (syncRoot)
			{
				callbackScheduled = false;
				if (!applyFailed && snapshot is not null && !isFinal && intermediateApplyCooldown > TimeSpan.Zero)
					nextIntermediateApplyAt = now().Add(intermediateApplyCooldown);

				// Restore a failed snapshot only when no newer snapshot replaced it while
				// the callback was running; newer state must always win.
				if (applyFailed && !isCanceled && snapshot is not null && pendingSnapshot is null)
				{
					pendingSnapshot = snapshot;
					pendingCancellationToken = cancellationToken;
					pendingSnapshotIsFinal = isFinal;
				}

				if (!isCanceled && pendingSnapshot is not null && (!applyFailed || !ReferenceEquals(pendingSnapshot, snapshot)))
				{
					callbackScheduled = true;
					nextCompletion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

					if (!pendingSnapshotIsFinal && (nextCooldown = GetRemainingCooldownLocked()) > TimeSpan.Zero)
					{
						nextCooldownCancellation = cooldownCancellation = new CancellationTokenSource();
					}
				}

				if (ReferenceEquals(this.activeApplyCompletion, activeApplyCompletion))
					this.activeApplyCompletion = null;
			}

			completion.TrySetResult(!applyFailed);
			activeApplyCompletion?.TrySetResult(true);

			if (nextCompletion is not null)
			{
				if (nextCooldownCancellation is null)
					_ = ScheduleCallbackAsync(nextCompletion);
				else
					_ = ScheduleAfterCooldownAsync(nextCompletion, nextCooldown!.Value, nextCooldownCancellation);
			}
		}
	}

	private TimeSpan GetRemainingCooldownLocked()
	{
		if (!nextIntermediateApplyAt.HasValue)
			return TimeSpan.Zero;

		var remaining = nextIntermediateApplyAt.Value - now();
		return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
	}

	private async Task ScheduleAfterCooldownAsync(
		TaskCompletionSource<bool> completion,
		TimeSpan delay,
		CancellationTokenSource cooldownSource)
	{
		try
		{
			await delayAsync(delay, cooldownSource.Token);
			ScheduleCallbackIfCurrent(completion, cooldownSource);
		}
		catch (OperationCanceledException) when (cooldownSource.IsCancellationRequested)
		{
			// A final snapshot cancels the cooldown so it can settle immediately.
			ScheduleCallbackIfCurrent(completion, cooldownSource);
		}
		catch (Exception ex)
		{
			lock (syncRoot)
			{
				if (ReferenceEquals(cooldownCancellation, cooldownSource))
					cooldownCancellation = null;
				callbackScheduled = false;
			}

			HandleError(ex);
			completion.TrySetResult(false);
		}
		finally
		{
			cooldownSource.Dispose();
		}
	}

	private void ScheduleCallbackIfCurrent(TaskCompletionSource<bool> completion, CancellationTokenSource cooldownSource)
	{
		bool shouldSchedule;

		lock (syncRoot)
		{
			if (ReferenceEquals(cooldownCancellation, cooldownSource))
				cooldownCancellation = null;

			shouldSchedule = !isCanceled
				&& callbackScheduled
				&& ReferenceEquals(scheduledCompletion, completion)
				&& pendingSnapshot is not null;
		}

		if (shouldSchedule)
			_ = ScheduleCallbackAsync(completion);
	}

	private void HandleError(Exception exception)
	{
		if (errorHandler is not null)
		{
			errorHandler(exception);
			return;
		}

		App.Logger.LogWarning(exception, "Enumeration snapshot publication failed.");
	}
}
