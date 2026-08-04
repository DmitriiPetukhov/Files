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

	private Task PublishBatchAsync(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batch);

		return snapshotGate.ExecuteAsync(async () =>
		{
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
			var accepted = session.TryReplaceFinal(items, cancellationToken, out var snapshot);
			if (!accepted)
				return false;

			await publishSnapshotAsync(snapshot!);
			return true;
		});
	}

}
