// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.Shared.Helpers;
using System.Collections.Generic;
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
	private readonly object overlaySyncRoot = new();
	private readonly Dictionary<FolderItemKey, FolderItem> baseItems = [];
	private readonly Dictionary<FolderItemKey, ListedItem> primaryItems = [];
	private readonly Dictionary<FolderItemKey, IReadOnlyList<ListedItem>> additionalItems = [];
	private readonly Dictionary<FolderItemKey, long> overlayRevisions = [];

	/// <summary>Creates a legacy UI item from a provider-neutral item snapshot.</summary>
	public ListedItem Project(FolderItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		lock (overlaySyncRoot)
			return GetOrCreatePrimaryItem(item);
	}

	/// <summary>Projects a full immutable state while applying the current compatibility overlay.</summary>
	/// <param name="state">Immutable provider-neutral state to project.</param>
	/// <returns>Primary and overlay-owned compatibility items in source order.</returns>
	public IReadOnlyList<ListedItem> ProjectState(FolderPublicationState state)
	{
		ArgumentNullException.ThrowIfNull(state);

		lock (overlaySyncRoot)
		{
			var result = new List<ListedItem>(state.Items.Length);
			foreach (var item in state.Items)
			{
				result.Add(GetOrCreatePrimaryItem(item));
				if (additionalItems.TryGetValue(item.Key, out var itemAdditionalItems))
					result.AddRange(itemAdditionalItems);
			}

			return result.AsReadOnly();
		}
	}

	/// <summary>Stores the stable legacy primary item and its compatibility-only additional entries.</summary>
	/// <param name="key">Canonical provider-neutral item identity.</param>
	/// <param name="primaryItem">Legacy primary item to reuse for later projections.</param>
	/// <param name="additionalItems">Additional entries such as alternate streams.</param>
	/// <param name="expectedRevision">Revision represented by the legacy work, when available.</param>
	public void ApplyLegacyOverlay(
		FolderItemKey key,
		ListedItem primaryItem,
		IReadOnlyCollection<ListedItem>? additionalItems = null,
		long? expectedRevision = null)
	{
		ArgumentNullException.ThrowIfNull(primaryItem);

		lock (overlaySyncRoot)
		{
			primaryItems[key] = primaryItem;
			overlayRevisions[key] = expectedRevision.GetValueOrDefault();
			if (additionalItems is null || additionalItems.Count == 0)
				this.additionalItems.Remove(key);
			else
				this.additionalItems[key] = Array.AsReadOnly(additionalItems.ToArray());
		}
	}

	/// <summary>Removes an overlay only when it still belongs to the expected legacy instance.</summary>
	/// <param name="key">Canonical provider-neutral item identity.</param>
	/// <param name="expectedPrimaryItem">Expected overlay instance, or <see langword="null"/> for no-op cleanup.</param>
	/// <param name="expectedRevision">Revision represented by the legacy work, when available.</param>
	public void RemoveLegacyOverlay(
		FolderItemKey key,
		ListedItem? expectedPrimaryItem,
		long? expectedRevision = null)
	{
		if (expectedPrimaryItem is null)
			return;

		lock (overlaySyncRoot)
		{
			if (primaryItems.TryGetValue(key, out var currentItem) &&
				ReferenceEquals(currentItem, expectedPrimaryItem) &&
				(expectedRevision is null || overlayRevisions.TryGetValue(key, out var currentRevision) && currentRevision == expectedRevision))
			{
				primaryItems.Remove(key);
				additionalItems.Remove(key);
				overlayRevisions.Remove(key);
			}
		}
	}

	private ListedItem GetOrCreatePrimaryItem(FolderItem item)
	{
		if (baseItems.TryGetValue(item.Key, out var previousItem) && !Equals(previousItem, item))
		{
			primaryItems.Remove(item.Key);
			additionalItems.Remove(item.Key);
			overlayRevisions.Remove(item.Key);
		}

		baseItems[item.Key] = item;
		if (primaryItems.TryGetValue(item.Key, out var existingItem))
			return existingItem;

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

		primaryItems.Add(item.Key, listedItem);
		return listedItem;
	}
}
