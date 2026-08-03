// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage;

internal sealed class Win32EnumerationPublicationGate<T> : IAsyncDisposable
{
	private readonly object syncRoot = new();
	private readonly SemaphoreSlim publicationSemaphore = new(1, 1);
	private readonly Func<IReadOnlyList<T>, Task> publishAsync;
	private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
	private readonly Action<Exception>? errorHandler;
	private readonly CancellationToken publicationCancellationToken;
	private readonly int initialBatchSize;
	private readonly int intermediateBatchSize;
	private readonly TimeSpan batchTimeout;

	// The payload can include alternate streams, while only primary items advance thresholds.
	private List<T> pendingItems = new();
	private int pendingPrimaryItemCount;
	private CancellationTokenSource? batchTimerCancellation;
	private bool hasPublishedFirstBatch;
	private bool isCompleted;
	private bool isCanceled;

	public Win32EnumerationPublicationGate(
		Func<IReadOnlyList<T>, Task> publishAsync,
		int initialBatchSize,
		int intermediateBatchSize,
		TimeSpan batchTimeout,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Action<Exception>? errorHandler = null,
		CancellationToken publicationCancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(publishAsync);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialBatchSize);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intermediateBatchSize);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchTimeout.Ticks);

		this.publishAsync = publishAsync;
		this.initialBatchSize = initialBatchSize;
		this.intermediateBatchSize = intermediateBatchSize;
		this.batchTimeout = batchTimeout;
		this.delayAsync = delayAsync ?? Task.Delay;
		this.errorHandler = errorHandler;
		this.publicationCancellationToken = publicationCancellationToken;
	}

	public Task AddAsync(T item, CancellationToken cancellationToken)
		=> AddAsync(item, countsTowardThreshold: true, cancellationToken);

	public Task AddAsync(T item, bool countsTowardThreshold, CancellationToken cancellationToken)
	{
		List<T>? batchToPublish = null;

		lock (syncRoot)
		{
			ThrowIfUnavailable();
			cancellationToken.ThrowIfCancellationRequested();

			pendingItems.Add(item);
			if (countsTowardThreshold)
				pendingPrimaryItemCount++;

			StartTimerIfNeededLocked();

			var batchSize = hasPublishedFirstBatch ? intermediateBatchSize : initialBatchSize;
			if (pendingPrimaryItemCount >= batchSize)
				batchToPublish = DetachBatchLocked();
		}

		return PublishBatchAsync(batchToPublish, cancellationToken);
	}

	public Task FlushAsync(CancellationToken cancellationToken)
	{
		List<T>? batchToPublish;

		lock (syncRoot)
		{
			if (isCanceled || cancellationToken.IsCancellationRequested)
				return Task.CompletedTask;

			batchToPublish = DetachBatchLocked();
		}

		return PublishBatchAsync(batchToPublish, cancellationToken);
	}

	public Task CompleteAsync(CancellationToken cancellationToken)
	{
		List<T>? batchToPublish;

		lock (syncRoot)
		{
			if (isCanceled || cancellationToken.IsCancellationRequested)
				return Task.CompletedTask;

			isCompleted = true;
			batchToPublish = DetachBatchLocked();
		}

		return PublishBatchAsync(batchToPublish, cancellationToken);
	}

	public async Task CancelAsync()
	{
		lock (syncRoot)
		{
			if (isCanceled)
				return;

			isCanceled = true;
			isCompleted = true;
			pendingItems.Clear();
			CancelTimerLocked();
		}

		await publicationSemaphore.WaitAsync();
		publicationSemaphore.Release();
	}

	public async ValueTask DisposeAsync()
	{
		await CancelAsync();
		publicationSemaphore.Dispose();
	}

	private void StartTimerIfNeededLocked()
	{
		if (batchTimerCancellation is not null)
			return;

		batchTimerCancellation = new CancellationTokenSource();
		_ = RunTimerAsync(batchTimerCancellation.Token);
	}

	private async Task RunTimerAsync(CancellationToken cancellationToken)
	{
		try
		{
			await delayAsync(batchTimeout, cancellationToken);
			await FlushAsync(publicationCancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			HandleError(ex);
		}
	}

	private List<T>? DetachBatchLocked()
	{
		if (pendingItems.Count == 0)
			return null;

		var batch = pendingItems;
		pendingItems = new List<T>();
		pendingPrimaryItemCount = 0;
		hasPublishedFirstBatch = true;
		CancelTimerLocked();
		return batch;
	}

	private async Task PublishBatchAsync(List<T>? batch, CancellationToken cancellationToken)
	{
		if (batch is null || batch.Count == 0)
			return;

		try
		{
			// Batches are detached under the state lock so enumeration can continue. The
			// semaphore serializes callbacks and lets cancellation wait for the active one.
			await publicationSemaphore.WaitAsync(cancellationToken);
			try
			{
				lock (syncRoot)
				{
					if (isCanceled)
						return;
				}

				await publishAsync(batch);
			}
			finally
			{
				publicationSemaphore.Release();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			HandleError(ex);
		}
	}

	private void CancelTimerLocked()
	{
		batchTimerCancellation?.Cancel();
		batchTimerCancellation?.Dispose();
		batchTimerCancellation = null;
	}

	private void ThrowIfUnavailable()
	{
		if (isCanceled)
			throw new OperationCanceledException();

		if (isCompleted)
			throw new InvalidOperationException("The enumeration publication gate is already complete.");
	}

	private void HandleError(Exception exception)
	{
		if (errorHandler is not null)
		{
			errorHandler(exception);
			return;
		}

		App.Logger.LogWarning(exception, "Win32 enumeration publication failed.");
	}
}
