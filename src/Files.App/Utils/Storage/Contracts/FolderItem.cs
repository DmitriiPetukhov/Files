// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Contracts;

/// <summary>Represents an immutable provider-neutral item snapshot.</summary>
/// <param name="Key">Stable provider-owned item identity.</param>
/// <param name="Name">Provider-neutral child name.</param>
/// <param name="Kind">Universal item kind.</param>
/// <param name="Metadata">Optional commonly used item metadata.</param>
/// <param name="ProviderData">Optional immutable provider-specific snapshot data.</param>
internal sealed record FolderItem(
	FolderItemKey Key,
	string Name,
	FolderItemKind Kind,
	FolderItemMetadata? Metadata,
	IProviderItemData? ProviderData);
