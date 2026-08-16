// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.Utils.Storage;

/// <summary>
/// Owns provider-neutral keyed folder state for one navigation.
/// </summary>
internal interface IFolderPublicationSession
{
	/// <summary>Accepts one source batch and creates a full accumulated state when it changes canonical state.</summary>
	/// <param name="batch">The completed source batch.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="state">The immutable accumulated state when accepted.</param>
	/// <returns><see langword="true"/> when the batch changed canonical state.</returns>
	bool TryAppend(
		FolderEnumerationBatch<FolderItem> batch,
		CancellationToken cancellationToken,
		out FolderPublicationState? state);

	/// <summary>Applies a late keyed replacement when its captured revision is current.</summary>
	/// <param name="key">Canonical item identity.</param>
	/// <param name="item">Replacement provider-neutral item.</param>
	/// <param name="expectedRevision">Revision captured when the update was queued.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="state">The immutable state when accepted.</param>
	/// <returns><see langword="true"/> when the update was accepted.</returns>
	bool TryApplyUpdate(
		FolderItemKey key,
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken,
		out FolderPublicationState? state);

	/// <summary>Gets the latest immutable state without changing the session.</summary>
	FolderPublicationState GetCurrentState();

	/// <summary>Gets the current revision for a canonical key.</summary>
	/// <param name="key">Canonical item identity.</param>
	/// <param name="revision">Current revision when the key exists.</param>
	/// <returns><see langword="true"/> when the key exists.</returns>
	bool TryGetRevision(FolderItemKey key, out long revision);

	/// <summary>Stops future mutations after source and enrichment settlement.</summary>
	void Complete();

	/// <summary>Rejects future mutations for a canceled navigation.</summary>
	void Cancel();
}

/// <summary>
/// Owns the canonical accepted items and their incremental ordered state for one navigation.
/// </summary>
/// <typeparam name="T">The item type being accumulated.</typeparam>
internal interface IFolderPublicationSession<T>
{
	/// <summary>
	/// Appends a completed batch and creates the next ordered snapshot.
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
	/// Rebuilds only the ordered index while retaining the canonical accepted items.
	/// </summary>
	/// <param name="itemComparer">The comparer for the new sort configuration.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="snapshot">The rebuilt snapshot when the operation is accepted.</param>
	/// <returns><see langword="true"/> when the rebuild is accepted.</returns>
	bool TryRebuildIndex(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot);

	/// <summary>
	/// Invalidates the session so stale callbacks cannot change its state.
	/// </summary>
	void Cancel();
}
