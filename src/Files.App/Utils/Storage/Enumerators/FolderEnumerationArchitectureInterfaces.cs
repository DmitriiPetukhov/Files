// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal interface IFolderNavigationService
{
	Task OpenAsync(string path, CancellationToken cancellationToken);

	Task CancelCurrentAsync();
}

internal interface IFolderEnumerationSourceFactory<T>
{
	bool CanHandle(string path);

	IFolderEnumerationSource<T> Create(string path);
}

internal interface IFolderEnumerationSource<T>
{
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken);
}

internal interface IFolderBatchPublisher<T>
{
	ValueTask AddAsync(
		T item,
		bool countsTowardPrimaryThreshold,
		CancellationToken cancellationToken);

	Task CompleteAsync(CancellationToken cancellationToken);

	Task CancelAsync();
}

internal interface IFolderPublicationCoordinator<T>
{
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken);

	bool TryPublishBatch(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken);

	bool TryPublishFinal(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken);

	Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false);

	Task CancelAsync();
}

internal interface IFolderPublicationSession<T>
{
	bool TryAppend(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot);

	bool TryReplaceFinal(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot);

	void Cancel();
}

internal interface IFolderSnapshotCoalescer<T>
{
	void Submit(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	void SubmitFinal(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false);

	Task CancelAsync();
}

internal interface IFolderSnapshotScheduler
{
	Task ScheduleAsync(Func<Task> callback);
}

internal interface IFolderSnapshotProjection<T>
{
	Task ApplyAsync(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);
}

internal interface IFolderItemEnrichmentService<T>
{
	Task EnqueueAsync(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken);
}

internal interface IFolderEnumerationDiagnostics
{
	void Record(
		string eventName,
		long navigationId,
		int payloadCount,
		int accumulatedCount,
		TimeSpan elapsed);

	void RecordFailure(
		string eventName,
		long navigationId,
		Exception exception,
		TimeSpan elapsed);
}
