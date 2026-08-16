// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Contracts;

/// <summary>Contains optional inexpensive metadata observed for an item.</summary>
/// <param name="SizeBytes">Observed item size, when available.</param>
/// <param name="CreatedUtc">Observed creation time, when available.</param>
/// <param name="ModifiedUtc">Observed modification time, when available.</param>
/// <param name="IsHidden">Whether the item is hidden from the normal listing.</param>
internal sealed record FolderItemMetadata(
	long? SizeBytes,
	DateTimeOffset? CreatedUtc,
	DateTimeOffset? ModifiedUtc,
	bool IsHidden = false);
