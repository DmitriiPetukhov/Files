// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage;

/// <summary>Receives accepted base items for bounded, navigation-scoped late enrichment.</summary>
internal interface IFolderPublicationEnrichment : IAsyncDisposable
{
	/// <summary>Queues one accepted item with its captured session revision.</summary>
	ValueTask EnqueueAsync(
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken);

	/// <summary>Waits for accepted work to settle before final state publication.</summary>
	Task CompleteAsync(CancellationToken cancellationToken);

	/// <summary>Cancels queued and active optional work.</summary>
	Task CancelAsync();
}
