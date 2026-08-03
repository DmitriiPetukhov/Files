// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

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
		ImmutableArray<T> batch,
		CancellationToken cancellationToken,
		out ImmutableArray<T>? snapshot);

	/// <summary>
	/// Replaces the current state with the authoritative final result.
	/// </summary>
	/// <param name="items">The complete accepted result.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="snapshot">The final ordered snapshot when accepted.</param>
	/// <returns><see langword="true"/> when the final result is accepted.</returns>
	bool TryReplaceFinal(
		ImmutableArray<T> items,
		CancellationToken cancellationToken,
		out ImmutableArray<T>? snapshot);

	/// <summary>
	/// Invalidates the session so stale callbacks cannot change its state.
	/// </summary>
	void Cancel();
}
