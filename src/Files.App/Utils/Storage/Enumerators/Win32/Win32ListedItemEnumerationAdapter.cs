// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Projections;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Projects the provider-neutral Win32 stream into the legacy ListedItem path.</summary>
internal sealed class Win32ListedItemEnumerationAdapter(
	IFolderEnumerationSource source,
	FolderItemListedItemProjection projection) : IFolderEnumerationSource<ListedItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);
		var allItems = new List<ListedItem>();

		await foreach (var batch in source.EnumerateAsync(cancellationToken))
		{
			var projectedItems = batch.Items
				.Select(projection.Project)
				.ToList();

			allItems.AddRange(projectedItems);
			await publishBatchAsync(projectedItems);
		}

		return allItems;
	}
}
