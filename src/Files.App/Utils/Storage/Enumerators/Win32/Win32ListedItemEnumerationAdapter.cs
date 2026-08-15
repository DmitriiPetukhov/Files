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
	private const int MainItemsPerPublication = 32;
	private static readonly TimeSpan PublicationInterval = TimeSpan.FromMilliseconds(500);

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
		var pendingItems = new List<ListedItem>();
		var pendingMainItemCount = 0;
		var publicationSampler = new IntervalSampler(PublicationInterval);
		IUserSettingsService? userSettingsService = null;
		IconWarmUpQueue? iconWarmUpQueue = null;
		ISizeProvider? folderSizeProvider = null;

		if (legacyRootPath is not null)
		{
			userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			iconWarmUpQueue = Ioc.Default.GetRequiredService<IconWarmUpQueue>();
			folderSizeProvider = Ioc.Default.GetRequiredService<ISizeProvider>();
		}

		await foreach (var batch in source.EnumerateAsync(cancellationToken))
		{
			foreach (var item in batch.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var materialized = legacyRootPath is not null
					? await MaterializeLegacyItemAsync(
						item,
						userSettingsService!,
						iconWarmUpQueue!,
						folderSizeProvider!,
						cancellationToken)
					: (Items: (IReadOnlyCollection<ListedItem>)new List<ListedItem> { projection.Project(item) }, AcceptedMainItemCount: 1);

				if (materialized.Items.Count == 0)
					continue;

				allItems.AddRange(materialized.Items);
				pendingItems.AddRange(materialized.Items);
				pendingMainItemCount += materialized.AcceptedMainItemCount;

				if (pendingMainItemCount >= MainItemsPerPublication || publicationSampler.CheckNow())
				{
					await publishBatchAsync(pendingItems.ToArray());
					pendingItems.Clear();
					pendingMainItemCount = 0;
				}
			}
		}

		if (pendingItems.Count > 0)
			await publishBatchAsync(pendingItems.ToArray());

		return allItems;
	}

	private async Task<(IReadOnlyCollection<ListedItem> Items, int AcceptedMainItemCount)> MaterializeLegacyItemAsync(
		FolderItem item,
		IUserSettingsService userSettingsService,
		IconWarmUpQueue iconWarmUpQueue,
		ISizeProvider folderSizeProvider,
		CancellationToken cancellationToken)
	{
		var foldersSettings = userSettingsService.FoldersSettingsService;
		var materializedItems = new List<ListedItem>();

		if (item.ProviderData is not Win32FolderItemData providerData)
		{
			materializedItems.Add(projection.Project(item));
			return (materializedItems, 1);
		}

		var findData = providerData.FindData;
		var fileAttributes = (FileAttributes)findData.dwFileAttributes;
		var isHidden = fileAttributes.HasFlag(FileAttributes.Hidden);
		var isSystem = fileAttributes.HasFlag(FileAttributes.System);
		var startsWithDot = findData.cFileName.StartsWith('.');
		if ((isHidden && (!foldersSettings.ShowHiddenItems || isSystem && !foldersSettings.ShowProtectedSystemFiles)) ||
			(startsWithDot && !foldersSettings.ShowDotFiles))
			return (materializedItems, 0);

		var isFolder = fileAttributes.HasFlag(FileAttributes.Directory);
		var listedItem = isFolder
			? await Win32StorageEnumerator.GetFolder(findData, legacyRootPath!, isGitRepo, cancellationToken)
			: await Win32StorageEnumerator.GetFile(findData, legacyRootPath!, isGitRepo, cancellationToken);
		if (listedItem is null)
			return (materializedItems, 0);

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

		return (materializedItems, 1);
	}
}
