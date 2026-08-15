// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.Shared.Helpers;
using System.IO;
using Windows.Storage;

namespace Files.App.Utils.Storage.Projections;

/// <summary>Projects provider-neutral folder items into the current UI item model.</summary>
internal sealed class FolderItemListedItemProjection(
	Func<string, string>? localize = null,
	Func<long, string>? formatSize = null,
	Func<string, ListedItem>? createListedItem = null)
{
	private readonly Func<string, string> localize = localize ?? (resourceKey => resourceKey.GetLocalizedResource());
	private readonly Func<long, string> formatSize = formatSize ?? (sizeBytes => sizeBytes.ToSizeString());
	private readonly Func<string, ListedItem> createListedItem = createListedItem ?? (folderRelativeId => new ListedItem(folderRelativeId));

	/// <summary>Creates a legacy UI item from a provider-neutral item snapshot.</summary>
	public ListedItem Project(FolderItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		var isFolder = item.Kind == FolderItemKind.Folder;
		var sizeBytes = item.Metadata?.SizeBytes;
		var fileExtension = isFolder ? null : Path.GetExtension(item.Name);
		var itemType = isFolder
			? localize(Strings.Folder)
			: string.IsNullOrEmpty(fileExtension)
				? localize(Strings.File)
				: $"{fileExtension.TrimStart('.')} {localize(Strings.File)}";

		var listedItem = createListedItem(string.Empty);
		listedItem.PrimaryItemAttribute = isFolder ? StorageItemTypes.Folder : StorageItemTypes.File;
		listedItem.FileExtension = fileExtension ?? string.Empty;
		listedItem.LoadFileIcon = false;
		listedItem.ItemNameRaw = item.Name;
		listedItem.ItemType = itemType;
		listedItem.ItemPath = item.Key.OpaqueId;
		listedItem.FileSize = !isFolder && sizeBytes.HasValue ? formatSize(sizeBytes.Value) : string.Empty;
		listedItem.FileSizeBytes = sizeBytes.GetValueOrDefault();

		if (item.Metadata?.CreatedUtc is { } createdUtc)
			listedItem.ItemDateCreatedReal = createdUtc;

		if (item.Metadata?.ModifiedUtc is { } modifiedUtc)
			listedItem.ItemDateModifiedReal = modifiedUtc;

		return listedItem;
	}
}
