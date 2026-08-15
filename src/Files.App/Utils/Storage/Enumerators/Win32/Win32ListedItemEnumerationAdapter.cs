// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Projections;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Projects the provider-neutral Win32 stream into the legacy ListedItem path.</summary>
internal sealed class Win32ListedItemEnumerationAdapter(
	Win32FolderEnumerationSource source,
	FolderItemListedItemProjection projection) : IFolderEnumerationSource<ListedItem>
{
	public Win32ListedItemEnumerationAdapter(
		string folderPath,
		IntPtr handle,
		WIN32_FIND_DATA firstFindData)
		: this(
			new Win32FolderEnumerationSource(folderPath, handle, firstFindData),
			new FolderItemListedItemProjection())
	{
	}

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);
		var allItems = new List<ListedItem>();

		try
		{
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
		finally
		{
			await source.DisposeAsync();
		}
	}
}
