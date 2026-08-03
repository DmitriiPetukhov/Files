// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

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
