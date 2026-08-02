// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage;

internal sealed class EnumerationSnapshotCoalescer<T>
{
	private readonly object syncRoot = new();
	private readonly Func<IReadOnlyList<T>, CancellationToken, Task> applyAsync;
	private readonly Func<Func<Task>, Task> scheduleAsync;
	private readonly Action<Exception>? errorHandler;

	private IReadOnlyList<T>? pendingSnapshot;
	private CancellationToken pendingCancellationToken;
	private TaskCompletionSource<bool>? scheduledCompletion;
	private bool callbackScheduled;
	private bool isCanceled;

	public EnumerationSnapshotCoalescer(
		Func<IReadOnlyList<T>, CancellationToken, Task> applyAsync,
		Func<Func<Task>, Task> scheduleAsync,
		Action<Exception>? errorHandler = null)
	{
		ArgumentNullException.ThrowIfNull(applyAsync);
		ArgumentNullException.ThrowIfNull(scheduleAsync);

		this.applyAsync = applyAsync;
		this.scheduleAsync = scheduleAsync;
		this.errorHandler = errorHandler;
	}

	public void Submit(IReadOnlyList<T> snapshot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		TaskCompletionSource<bool>? completion = null;

		lock (syncRoot)
		{
			if (isCanceled || cancellationToken.IsCancellationRequested)
				return;

			pendingSnapshot = snapshot.ToArray();
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
		lock (syncRoot)
		{
			isCanceled = true;
			pendingSnapshot = null;
		}
	}

	public async Task DrainAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			Task? completion;

			lock (syncRoot)
			{
				if (!callbackScheduled && pendingSnapshot is null)
					return;

				completion = scheduledCompletion?.Task;
			}

			if (completion is null)
				return;

			await completion.WaitAsync(cancellationToken);
		}
	}

	private async Task ScheduleCallbackAsync(TaskCompletionSource<bool> completion)
	{
		try
		{
			await scheduleAsync(() => ApplyScheduledSnapshotAsync(completion));
		}
		catch (Exception ex)
		{
			HandleError(ex);

			lock (syncRoot)
			{
				callbackScheduled = false;
				pendingSnapshot = null;
			}

			completion.TrySetResult(true);
		}
	}

	private async Task ApplyScheduledSnapshotAsync(TaskCompletionSource<bool> completion)
	{
		IReadOnlyList<T>? snapshot;
		CancellationToken cancellationToken;

		lock (syncRoot)
		{
			snapshot = pendingSnapshot;
			cancellationToken = pendingCancellationToken;
			pendingSnapshot = null;
		}

		try
		{
			if (snapshot is not null && !cancellationToken.IsCancellationRequested && !isCanceled)
				await applyAsync(snapshot, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || isCanceled)
		{
		}
		catch (Exception ex)
		{
			HandleError(ex);
		}
		finally
		{
			TaskCompletionSource<bool>? nextCompletion = null;

			lock (syncRoot)
			{
				callbackScheduled = false;

				if (!isCanceled && pendingSnapshot is not null)
				{
					callbackScheduled = true;
					nextCompletion = scheduledCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				}
			}

			completion.TrySetResult(true);

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
