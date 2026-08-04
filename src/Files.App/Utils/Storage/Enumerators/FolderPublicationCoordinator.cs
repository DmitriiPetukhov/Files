// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Coordinates one source enumeration with its canonical publication session.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal sealed class FolderPublicationCoordinator<T> : IFolderPublicationCoordinator<T>
{
	private readonly IFolderPublicationSession<T> session;
	private readonly FolderPublicationSnapshotGate snapshotGate = new();
	private readonly Func<IReadOnlyCollection<T>, Task> publishSnapshotAsync;
	private bool isActive = true;

	public FolderPublicationCoordinator(
		IComparer<T> itemComparer,
		Func<IReadOnlyCollection<T>, Task> publishSnapshotAsync)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);
		ArgumentNullException.ThrowIfNull(publishSnapshotAsync);

		session = new FolderPublicationSession<T>(itemComparer);
		this.publishSnapshotAsync = publishSnapshotAsync;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);

		var finalItems = await source.EnumerateAsync(
			batch => PublishBatchAsync(batch, cancellationToken),
			cancellationToken);

		await PublishFinalAsync(finalItems, cancellationToken);

		return finalItems;
	}

	/// <inheritdoc />
	public Task<bool> TryRebuildIndexAsync(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);

		return snapshotGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var rebuildResult = await FolderPublicationSessionWorker.RebuildIndexAsync(
				session,
				itemComparer,
				cancellationToken);

			if (!rebuildResult.Accepted)
				return false;

			await publishSnapshotAsync(rebuildResult.Snapshot!);
			return true;
		});
	}

	/// <inheritdoc />
	public Task CancelAsync()
	{
		return snapshotGate.ExecuteAsync(() =>
		{
			isActive = false;
			session.Cancel();
			return Task.FromResult(true);
		});
	}

	private Task PublishBatchAsync(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batch);

		return snapshotGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var accepted = session.TryAppend(batch, cancellationToken, out var snapshot);
			if (!accepted)
				return false;

			await publishSnapshotAsync(snapshot!);
			return true;
		});
	}

	private Task PublishFinalAsync(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(items);

		return snapshotGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var accepted = session.TryReplaceFinal(items, cancellationToken, out var snapshot);
			if (!accepted)
				return false;

			await publishSnapshotAsync(snapshot!);
			return true;
		});
	}

	private bool CanPublish(CancellationToken cancellationToken)
		=> isActive && !cancellationToken.IsCancellationRequested;
}
