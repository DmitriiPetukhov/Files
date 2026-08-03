// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Owns the lifecycle of the active folder navigation.
/// </summary>
internal interface IFolderNavigationService
{
	/// <summary>
	/// Starts navigation to the specified folder.
	/// </summary>
	/// <param name="path">The source-independent folder path.</param>
	/// <param name="cancellationToken">The token for the navigation request.</param>
	Task OpenAsync(string path, CancellationToken cancellationToken);

	/// <summary>
	/// Cancels the active navigation and prevents its late results from reaching the UI.
	/// </summary>
	Task CancelCurrentAsync();
}

/// <summary>
/// Selects an enumeration source for a folder without exposing provider details to the UI.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal interface IFolderEnumerationSourceFactory<T>
{
	/// <summary>
	/// Determines whether this factory can enumerate the specified folder.
	/// </summary>
	/// <param name="path">The folder path to inspect.</param>
	/// <returns><see langword="true"/> when this factory supports the folder.</returns>
	bool CanHandle(string path);

	/// <summary>
	/// Creates a source for the specified folder.
	/// </summary>
	/// <param name="path">The folder path to enumerate.</param>
	/// <returns>A provider-specific source behind the common contract.</returns>
	IFolderEnumerationSource<T> Create(string path);
}

/// <summary>
/// Reads one provider and reports completed batches plus an authoritative final result.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal interface IFolderEnumerationSource<T>
{
	/// <summary>
	/// Enumerates the folder on a background context.
	/// </summary>
	/// <param name="publishBatchAsync">Receives completed non-empty batches that may be shown early.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns>The complete accepted result for the navigation.</returns>
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken);
}

/// <summary>
/// Applies a provider-specific batching policy before handing items to publication coordination.
/// </summary>
/// <typeparam name="T">The item type being batched.</typeparam>
internal interface IFolderBatchPublisher<T>
{
	/// <summary>
	/// Adds one item to the pending batch.
	/// </summary>
	/// <param name="item">The accepted item.</param>
	/// <param name="countsTowardPrimaryThreshold">Whether the item advances the primary threshold.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	ValueTask AddAsync(
		T item,
		bool countsTowardPrimaryThreshold,
		CancellationToken cancellationToken);

	/// <summary>
	/// Publishes any remaining completed items and marks the batcher complete.
	/// </summary>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task CompleteAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Stops batching and rejects late items or timer callbacks.
	/// </summary>
	Task CancelAsync();
}

/// <summary>
/// Coordinates source enumeration, canonical state, snapshot publication, and final settlement.
/// </summary>
/// <typeparam name="T">The item type shown by the folder projection.</typeparam>
internal interface IFolderPublicationCoordinator<T>
{
	/// <summary>
	/// Runs the source and publishes intermediate and final snapshots.
	/// </summary>
	/// <param name="source">The provider-neutral enumeration source.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns>The authoritative complete result.</returns>
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken);

	/// <summary>
	/// Merges a completed intermediate batch into the canonical session.
	/// </summary>
	/// <param name="batch">The completed non-empty batch.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns><see langword="true"/> when the batch belongs to the active session.</returns>
	bool TryPublishBatch(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken);

	/// <summary>
	/// Replaces the canonical session with the authoritative final result.
	/// </summary>
	/// <param name="items">The complete accepted result.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns><see langword="true"/> when the final result belongs to the active session.</returns>
	bool TryPublishFinal(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken);

	/// <summary>
	/// Waits for pending snapshot application and optionally retries the final snapshot once.
	/// </summary>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="retryPendingSnapshot">Whether the final pending snapshot may be retried.</param>
	Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false);

	/// <summary>
	/// Cancels the session and waits for owned publication work to stop.
	/// </summary>
	Task CancelAsync();
}

/// <summary>
/// Owns the canonical accepted items and their incremental ordered state for one navigation.
/// </summary>
/// <typeparam name="T">The item type being accumulated.</typeparam>
internal interface IFolderPublicationSession<T>
{
	/// <summary>
	/// Appends a completed batch and creates the next immutable snapshot.
	/// </summary>
	/// <param name="batch">The completed batch to merge.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="snapshot">The new snapshot when the batch is accepted.</param>
	/// <returns><see langword="true"/> when the batch is accepted.</returns>
	bool TryAppend(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot);

	/// <summary>
	/// Replaces the current state with the authoritative final result.
	/// </summary>
	/// <param name="items">The complete accepted result.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="snapshot">The final ordered snapshot when accepted.</param>
	/// <returns><see langword="true"/> when the final result is accepted.</returns>
	bool TryReplaceFinal(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot);

	/// <summary>
	/// Invalidates the session so stale callbacks cannot change its state.
	/// </summary>
	void Cancel();
}

/// <summary>
/// Coalesces worker-produced snapshots before they are scheduled for UI application.
/// </summary>
/// <typeparam name="T">The item type in each snapshot.</typeparam>
internal interface IFolderSnapshotCoalescer<T>
{
	/// <summary>
	/// Submits an intermediate snapshot, allowing newer snapshots to replace it.
	/// </summary>
	/// <param name="snapshot">The immutable ordered snapshot.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	void Submit(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	/// <summary>
	/// Submits an authoritative final snapshot that bypasses intermediate throttling.
	/// </summary>
	/// <param name="snapshot">The immutable final snapshot.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	void SubmitFinal(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	/// <summary>
	/// Waits for scheduled application and optionally retries the pending final snapshot.
	/// </summary>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="retryPendingSnapshot">Whether one final retry is permitted.</param>
	Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false);

	/// <summary>
	/// Cancels pending scheduling and completes owned work as canceled navigation flow.
	/// </summary>
	Task CancelAsync();
}

/// <summary>
/// Schedules one snapshot callback on the UI dispatcher.
/// </summary>
internal interface IFolderSnapshotScheduler
{
	/// <summary>
	/// Schedules the callback and reports enqueue or callback failures to the caller.
	/// </summary>
	/// <param name="callback">The callback that applies the current snapshot.</param>
	Task ScheduleAsync(Func<Task> callback);
}

/// <summary>
/// Applies source-independent snapshots to the UI projection.
/// </summary>
/// <typeparam name="T">The item type shown by the projection.</typeparam>
internal interface IFolderSnapshotProjection<T>
{
	/// <summary>
	/// Applies one snapshot through a bounded bulk UI operation.
	/// </summary>
	/// <param name="snapshot">The ordered snapshot to display.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task ApplyAsync(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);
}

/// <summary>
/// Performs optional metadata and icon work after the initial snapshot is available.
/// </summary>
/// <typeparam name="T">The item type being enriched.</typeparam>
internal interface IFolderItemEnrichmentService<T>
{
	/// <summary>
	/// Queues non-blocking enrichment for the supplied items.
	/// </summary>
	/// <param name="items">The items eligible for enrichment.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task EnqueueAsync(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken);
}

/// <summary>
/// Records safe, navigation-correlated enumeration and publication diagnostics.
/// </summary>
internal interface IFolderEnumerationDiagnostics
{
	/// <summary>
	/// Records a normal lifecycle or publication event.
	/// </summary>
	/// <param name="eventName">The bounded event name.</param>
	/// <param name="navigationId">The navigation correlation identifier.</param>
	/// <param name="payloadCount">The number of items in the current payload.</param>
	/// <param name="accumulatedCount">The number of accepted items so far.</param>
	/// <param name="elapsed">The elapsed time since navigation started.</param>
	void Record(
		string eventName,
		long navigationId,
		int payloadCount,
		int accumulatedCount,
		TimeSpan elapsed);

	/// <summary>
	/// Records a failure without requiring callers to expose paths or provider secrets.
	/// </summary>
	/// <param name="eventName">The bounded failure event name.</param>
	/// <param name="navigationId">The navigation correlation identifier.</param>
	/// <param name="exception">The failure to record.</param>
	/// <param name="elapsed">The elapsed time since navigation started.</param>
	void RecordFailure(
		string eventName,
		long navigationId,
		Exception exception,
		TimeSpan elapsed);
}
