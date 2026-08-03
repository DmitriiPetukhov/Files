// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

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
	Task<ImmutableArray<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken);

	/// <summary>
	/// Merges a completed intermediate batch into the canonical session.
	/// </summary>
	/// <param name="batch">The completed non-empty batch.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns><see langword="true"/> when the batch belongs to the active session.</returns>
	bool TryPublishBatch(
		ImmutableArray<T> batch,
		CancellationToken cancellationToken);

	/// <summary>
	/// Replaces the canonical session with the authoritative final result.
	/// </summary>
	/// <param name="items">The complete accepted result.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns><see langword="true"/> when the final result belongs to the active session.</returns>
	bool TryPublishFinal(
		ImmutableArray<T> items,
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
