// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class FolderPublicationCoordinator<T>
{
	private static readonly TimeSpan defaultIntermediateApplyCooldown = TimeSpan.FromMilliseconds(100);
	private readonly FolderPublicationSession<T> publicationSession;
	private readonly EnumerationSnapshotCoalescer<T> snapshotCoalescer;

	public FolderPublicationCoordinator(
		IComparer<T> comparer,
		Func<IReadOnlyCollection<T>, CancellationToken, Task> applyAsync,
		IFolderSnapshotScheduler scheduler,
		Func<T, bool>? countsTowardPrimary = null,
		TimeSpan? intermediateApplyCooldown = null,
		Action<FolderPublicationDiagnosticEvent>? diagnosticSink = null,
		Action<Exception>? errorHandler = null,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Func<DateTimeOffset>? now = null)
	{
		ArgumentNullException.ThrowIfNull(comparer);
		ArgumentNullException.ThrowIfNull(applyAsync);
		ArgumentNullException.ThrowIfNull(scheduler);

		var diagnostics = new FolderPublicationDiagnostics(diagnosticSink);
		publicationSession = new FolderPublicationSession<T>(comparer, countsTowardPrimary, diagnostics);
		snapshotCoalescer = new EnumerationSnapshotCoalescer<T>(
			async (snapshot, cancellationToken) =>
			{
				await applyAsync(snapshot, cancellationToken);
				var counts = publicationSession.GetCounts();
				diagnostics.Debug("coalesced", snapshot.Count, counts.AccumulatedCount, counts.PrimaryCount);
			},
			scheduler,
			exception =>
			{
				var counts = publicationSession.GetCounts();
				diagnostics.Warning("failed", 0, counts.AccumulatedCount, counts.PrimaryCount, exception);
				errorHandler?.Invoke(exception);
			},
			intermediateApplyCooldown ?? defaultIntermediateApplyCooldown,
			delayAsync,
			now);
	}

	public bool TryPublishBatch(IReadOnlyCollection<T> batch, CancellationToken cancellationToken)
	{
		if (!publicationSession.TryAppend(batch, cancellationToken, out var snapshot))
			return false;

		snapshotCoalescer.Submit(snapshot!, cancellationToken);
		return true;
	}

	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken,
		IComparer<T>? finalComparer = null)
	{
		ArgumentNullException.ThrowIfNull(source);

		var finalItems = await source.EnumerateAsync(
			batch =>
			{
				TryPublishBatch(batch, cancellationToken);
				return Task.CompletedTask;
			},
			cancellationToken);

		if (TryPublishFinal(finalItems, finalComparer, cancellationToken, out var finalSnapshot))
		{
			await DrainAsync(cancellationToken, retryPendingSnapshot: true);
			return finalSnapshot!;
		}

		return finalItems;
	}

	public bool TryPublishFinal(IReadOnlyCollection<T> items, CancellationToken cancellationToken)
		=> TryPublishFinal(items, finalComparer: null, cancellationToken, out _);

	public bool TryPublishFinal(IReadOnlyCollection<T> items, IComparer<T>? finalComparer, CancellationToken cancellationToken)
		=> TryPublishFinal(items, finalComparer, cancellationToken, out _);

	public bool TryPublishFinal(
		IReadOnlyCollection<T> items,
		IComparer<T>? finalComparer,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot)
	{
		if (!publicationSession.TryReplaceFinal(items, finalComparer, cancellationToken, out var finalSnapshot))
		{
			snapshot = null;
			return false;
		}

		snapshot = finalSnapshot;
		snapshotCoalescer.SubmitFinal(finalSnapshot!, cancellationToken);
		return true;
	}

	public Task DrainAsync(CancellationToken cancellationToken, bool retryPendingSnapshot = false)
		=> snapshotCoalescer.DrainAsync(cancellationToken, retryPendingSnapshot);

	public async Task CancelAsync()
	{
		publicationSession.Cancel();
		await snapshotCoalescer.CancelAsync();
	}
}
