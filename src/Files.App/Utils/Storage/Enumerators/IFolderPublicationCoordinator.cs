// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.Utils.Storage;

/// <summary>
/// Coordinates provider-neutral folder state publication for one navigation.
/// </summary>
internal interface IFolderPublicationCoordinator : IAsyncDisposable
{
	/// <summary>Reads immutable states produced by the active source lifecycle.</summary>
	/// <param name="cancellationToken">Token that stops the state reader.</param>
	/// <returns>A single-reader state stream.</returns>
	IAsyncEnumerable<FolderPublicationState> ReadStates(
		CancellationToken cancellationToken = default);

	/// <summary>Consumes accepted provider-neutral batches and settles the terminal state.</summary>
	/// <param name="batches">Cheap batches from the provider publication adapter.</param>
	/// <param name="cancellationToken">Token that stops the navigation.</param>
	Task EnumerateAsync(
		IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> batches,
		CancellationToken cancellationToken);

	/// <summary>Attaches the bounded enrichment owner before enumeration starts.</summary>
	/// <param name="enrichment">Navigation-scoped optional enrichment.</param>
	/// <returns><see langword="true"/> when the owner was attached.</returns>
	bool TrySetEnrichment(IFolderPublicationEnrichment enrichment);

	/// <summary>Submits a revision-checked late provider-neutral update.</summary>
	/// <param name="key">Canonical item identity.</param>
	/// <param name="item">Replacement item.</param>
	/// <param name="expectedRevision">Revision captured when work was queued.</param>
	/// <param name="cancellationToken">Token for the active navigation.</param>
	/// <returns><see langword="true"/> when the update produced a state.</returns>
	Task<bool> TryApplyUpdateAsync(
		FolderItemKey key,
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken);

	/// <summary>Cancels source, state, and enrichment lifecycle work.</summary>
	Task CancelAsync();
}

/// <summary>
/// Coordinates source enumeration, canonical state, and snapshot publication for one navigation.
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
	/// Rebuilds the ordered index for a same-folder sort change without restarting enumeration.
	/// </summary>
	/// <param name="itemComparer">The comparer for the new sort configuration.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns><see langword="true"/> when the rebuild and snapshot publication are accepted.</returns>
	Task<bool> TryRebuildIndexAsync(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken);

	/// <summary>
	/// Cancels the coordinator and rejects later source callbacks.
	/// </summary>
	Task CancelAsync();
}
