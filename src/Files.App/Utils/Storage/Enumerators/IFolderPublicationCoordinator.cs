// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

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
