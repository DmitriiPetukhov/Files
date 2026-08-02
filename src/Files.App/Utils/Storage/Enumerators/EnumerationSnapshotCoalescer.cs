// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage;

internal sealed class EnumerationSnapshotCoalescer<T>
{
	private readonly object syncRoot = new();
	private readonly Func<IReadOnlyList<T>, CancellationToken, Task> applyAsync;
	private readonly IFolderSnapshotScheduler scheduler;
	private readonly Action<Exception>? errorHandler;

	private IReadOnlyCollection<T>? pendingSnapshot;
	private CancellationToken pendingCancellationToken;
	private TaskCompletionSource<bool>? scheduledCompletion;
	private bool callbackScheduled;
	private bool isCanceled;

	public EnumerationSnapshotCoalescer(
		Func<IReadOnlyList<T>, CancellationToken, Task> applyAsync,
		IFolderSnapshotScheduler scheduler,
		Action<Exception>? errorHandler = null)
	{
		ArgumentNullException.ThrowIfNull(applyAsync);
		ArgumentNullException.ThrowIfNull(scheduler);

		this.applyAsync = applyAsync;
		this.scheduler = scheduler;
		this.errorHandler = errorHandler;
	}

	public void Submit(IReadOnlyCollection<T> snapshot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		TaskCompletionSource<bool>? completion = null;

		lock (syncRoot)
		{
			if (isCanceled || cancellationToken.IsCancellationRequested)
				return;

			pendingSnapshot = snapshot;
			pendingCancellationToken = cancellationToken;

			if (!callbackScheduled)
			{
				callbackScheduled = true;
				completion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			}
		}

		if (completion is not null)
			_ = ScheduleCallbackAsync(completion);
	}

	public void Cancel()
	{
		TaskCompletionSource<bool>? completion;

		lock (syncRoot)
		{
			isCanceled = true;
			pendingSnapshot = null;
			callbackScheduled = false;
			completion = scheduledCompletion;
			scheduledCompletion = null;
		}

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

		lock (syncRoot)
		{
			snapshot = pendingSnapshot;
			cancellationToken = pendingCancellationToken;
			pendingSnapshot = null;
		}

		var applyFailed = false;

		try
		{
			if (snapshot is not null && !cancellationToken.IsCancellationRequested && !isCanceled)
				await applyAsync(snapshot as IReadOnlyList<T> ?? snapshot.ToArray(), cancellationToken);
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

			lock (syncRoot)
			{
				callbackScheduled = false;

				if (applyFailed && !isCanceled && snapshot is not null && pendingSnapshot is null)
				{
					pendingSnapshot = snapshot;
					pendingCancellationToken = cancellationToken;
				}

				if (!isCanceled && pendingSnapshot is not null && (!applyFailed || !ReferenceEquals(pendingSnapshot, snapshot)))
				{
					callbackScheduled = true;
					nextCompletion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				}
			}

			completion.TrySetResult(!applyFailed);

			if (nextCompletion is not null)
				_ = ScheduleCallbackAsync(nextCompletion);
		}
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
