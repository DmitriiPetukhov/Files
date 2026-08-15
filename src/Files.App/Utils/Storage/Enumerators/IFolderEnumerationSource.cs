// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Enumerators;

/// <summary>Provides bounded item batches and current-item resolution for one bound folder.</summary>
internal interface IFolderEnumerationSource : IAsyncDisposable
{
	/// <summary>Enumerates the folder as an ordered asynchronous batch stream.</summary>
	/// <param name="cancellationToken">Token used to stop the enumeration.</param>
	/// <returns>The batches produced for the bound folder.</returns>
	IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		CancellationToken cancellationToken = default);

	/// <summary>Resolves an item identity to its current snapshot, when it exists.</summary>
	/// <param name="itemKey">Identity of the item to resolve.</param>
	/// <param name="cancellationToken">Token used to stop resolution.</param>
	/// <returns>The current item, or <see langword="null"/> when it is unavailable.</returns>
	ValueTask<FolderItem?> ResolveAsync(
		FolderItemKey itemKey,
		CancellationToken cancellationToken = default);
}
