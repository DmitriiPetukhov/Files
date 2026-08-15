// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Projections;
using Files.App.Services.SizeProvider;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Projects the Win32 source into legacy items while temporarily preserving legacy behavior.</summary>
internal sealed class Win32ListedItemEnumerationAdapter : IFolderEnumerationSource<ListedItem>
{
	private readonly IFolderEnumerationSource source;
	private readonly FolderItemListedItemProjection projection;
	private readonly string? legacyRootPath;
	private readonly bool isGitRepo;

	public Win32ListedItemEnumerationAdapter(
		IFolderEnumerationSource source,
		FolderItemListedItemProjection projection,
		string? legacyRootPath = null,
		bool isGitRepo = false)
	{
		this.source = source ?? throw new ArgumentNullException(nameof(source));
		this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
		this.legacyRootPath = legacyRootPath;
		this.isGitRepo = isGitRepo;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);
		var allItems = new List<ListedItem>();

		await foreach (var batch in source.EnumerateAsync(cancellationToken))
		{
			var projectedItems = legacyRootPath is not null
				? await MaterializeLegacyItemsAsync(batch.Items, cancellationToken)
				: batch.Items.Select(projection.Project).ToList();

			if (projectedItems.Count == 0)
				continue;

			allItems.AddRange(projectedItems);
			await publishBatchAsync(projectedItems);
		}

		return allItems;
	}

	private async Task<IReadOnlyCollection<ListedItem>> MaterializeLegacyItemsAsync(
		IReadOnlyCollection<FolderItem> items,
		CancellationToken cancellationToken)
	{
		var foldersSettings = Ioc.Default.GetRequiredService<IUserSettingsService>().FoldersSettingsService;
		var iconWarmUpQueue = Ioc.Default.GetRequiredService<IconWarmUpQueue>();
		var folderSizeProvider = Ioc.Default.GetRequiredService<ISizeProvider>();
		var materializedItems = new List<ListedItem>();

		foreach (var item in items)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (item.ProviderData is not Win32FolderItemData providerData)
			{
				materializedItems.Add(projection.Project(item));
				continue;
			}

			var findData = providerData.FindData;
			var fileAttributes = (FileAttributes)findData.dwFileAttributes;
			var isHidden = fileAttributes.HasFlag(FileAttributes.Hidden);
			var isSystem = fileAttributes.HasFlag(FileAttributes.System);
			var startsWithDot = findData.cFileName.StartsWith('.');
			if ((isHidden && (!foldersSettings.ShowHiddenItems || isSystem && !foldersSettings.ShowProtectedSystemFiles)) ||
				(startsWithDot && !foldersSettings.ShowDotFiles))
				continue;

			var isFolder = fileAttributes.HasFlag(FileAttributes.Directory);
			var listedItem = isFolder
				? await Win32StorageEnumerator.GetFolder(findData, legacyRootPath!, isGitRepo, cancellationToken)
				: await Win32StorageEnumerator.GetFile(findData, legacyRootPath!, isGitRepo, cancellationToken);
			if (listedItem is null)
				continue;

			materializedItems.Add(listedItem);
			iconWarmUpQueue.TryQueue(listedItem, isFolder, cancellationToken);

			if (foldersSettings.AreAlternateStreamsVisible)
			{
				materializedItems.AddRange(
					Win32Helper.GetAlternateStreams(listedItem.ItemPath)
						.Select(stream => Win32StorageEnumerator.GetAlternateStream(stream, listedItem)));
			}

			if (isFolder && foldersSettings.CalculateFolderSizes)
			{
				if (folderSizeProvider.TryGetSize(listedItem.ItemPath, out var size))
				{
					listedItem.FileSizeBytes = (long)size;
					listedItem.FileSize = size.ToSizeString();
				}

				_ = folderSizeProvider.UpdateAsync(listedItem.ItemPath, cancellationToken);
			}
		}

		return materializedItems;
	}
}
