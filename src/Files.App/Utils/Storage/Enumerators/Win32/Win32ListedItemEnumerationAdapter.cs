// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
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
		var scratchItems = ListedItemArrayPool.Shared.Rent();
		IUserSettingsService? userSettingsService = null;
		IconWarmUpQueue? iconWarmUpQueue = null;
		ISizeProvider? folderSizeProvider = null;

		if (legacyRootPath is not null)
		{
			try
			{
				userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
				iconWarmUpQueue = Ioc.Default.GetRequiredService<IconWarmUpQueue>();
				folderSizeProvider = Ioc.Default.GetRequiredService<ISizeProvider>();
			}
			catch
			{
				ListedItemArrayPool.Shared.Return(scratchItems);
				throw;
			}
		}

		try
		{
			await foreach (var batch in source.EnumerateAsync(cancellationToken))
			{
				foreach (var item in batch.Items)
				{
					cancellationToken.ThrowIfCancellationRequested();
					(ListedItem[] Buffer, int Count, int AcceptedMainItemCount) materialized;
					if (legacyRootPath is null)
					{
						materialized = (scratchItems, 1, 1);
					}
					else
					{
						try
						{
							materialized = await MaterializeLegacyItemAsync(
								item,
								userSettingsService!,
								iconWarmUpQueue!,
								folderSizeProvider!,
								scratchItems,
								cancellationToken);
						}
						catch (Exception ex) when (ex is not OperationCanceledException)
						{
							LogLegacyMaterializationFailure(item, ex);
							throw;
						}
					}

					scratchItems = materialized.Buffer;
					if (legacyRootPath is null)
						scratchItems[0] = projection.Project(item);

					if (materialized.Count == 0)
						continue;

					for (var index = 0; index < materialized.Count; index++)
					{
						allItems.Add(scratchItems[index]);
						pendingItems.Add(scratchItems[index]);
					}

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
		finally
		{
			ListedItemArrayPool.Shared.Return(scratchItems);
		}
	}

	private async ValueTask<(ListedItem[] Buffer, int Count, int AcceptedMainItemCount)> MaterializeLegacyItemAsync(
		FolderItem item,
		IUserSettingsService userSettingsService,
		IconWarmUpQueue iconWarmUpQueue,
		ISizeProvider folderSizeProvider,
		ListedItem[] buffer,
		CancellationToken cancellationToken)
	{
		var originalBuffer = buffer;
		try
		{
			var foldersSettings = userSettingsService.FoldersSettingsService;

			if (item.ProviderData is not Win32FolderItemData providerData)
			{
				buffer[0] = projection.Project(item);
				return (buffer, 1, 1);
			}

			var findData = providerData.FindData;
			var fileAttributes = (FileAttributes)findData.dwFileAttributes;
			var isHidden = fileAttributes.HasFlag(FileAttributes.Hidden);
			var isSystem = fileAttributes.HasFlag(FileAttributes.System);
			var startsWithDot = findData.cFileName.StartsWith('.');
			if ((isHidden && (!foldersSettings.ShowHiddenItems || isSystem && !foldersSettings.ShowProtectedSystemFiles)) ||
				(startsWithDot && !foldersSettings.ShowDotFiles))
				return (buffer, 0, 0);

			var isFolder = fileAttributes.HasFlag(FileAttributes.Directory);
			var listedItem = isFolder
				? await Win32StorageEnumerator.GetFolder(findData, legacyRootPath!, isGitRepo, cancellationToken)
				: await Win32StorageEnumerator.GetFile(findData, legacyRootPath!, isGitRepo, cancellationToken);
			if (listedItem is null)
				return (buffer, 0, 0);

			var itemCount = 1;
			buffer[0] = listedItem;
			iconWarmUpQueue.TryQueue(listedItem, isFolder, cancellationToken);

			if (foldersSettings.AreAlternateStreamsVisible)
			{
				foreach (var stream in Win32Helper.GetAlternateStreams(listedItem.ItemPath))
				{
					buffer = EnsureCapacity(buffer, itemCount + 1, itemCount);
					buffer[itemCount++] = Win32StorageEnumerator.GetAlternateStream(stream, listedItem);
				}
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

			if (!ReferenceEquals(buffer, originalBuffer))
				ListedItemArrayPool.Shared.Return(originalBuffer);

			return (buffer, itemCount, 1);
		}
		catch
		{
			if (!ReferenceEquals(buffer, originalBuffer))
				ListedItemArrayPool.Shared.Return(buffer);

			throw;
		}
	}

	private static ListedItem[] EnsureCapacity(ListedItem[] buffer, int requiredCapacity, int itemCount)
	{
		if (buffer.Length >= requiredCapacity)
			return buffer;

		var expandedBuffer = new ListedItem[Math.Max(requiredCapacity, buffer.Length * 2)];
		Array.Copy(buffer, expandedBuffer, itemCount);
		return expandedBuffer;
	}

	private static void LogLegacyMaterializationFailure(FolderItem item, Exception exception)
	{
		App.Logger.LogWarning(
			exception,
			"Win32 legacy item materialization failed. Path={Path} ErrorType={ErrorType} NativeErrorCode={NativeErrorCode}",
			LogPathHelper.GetPathIdentifier(item.Key.OpaqueId),
			exception.GetType().Name,
			(exception as Win32Exception)?.NativeErrorCode);
	}
}
