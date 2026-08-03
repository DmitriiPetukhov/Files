// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Performs optional metadata and icon work after the initial snapshot is available.
/// </summary>
/// <typeparam name="T">The item type being enriched.</typeparam>
internal interface IFolderItemEnrichmentService<T>
{
	/// <summary>
	/// Queues non-blocking enrichment for the supplied items.
	/// </summary>
	/// <param name="items">The items eligible for enrichment.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task EnqueueAsync(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken);
}
