// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Contains one enriched legacy primary item and compatibility-only additional entries.</summary>
internal sealed record Win32LegacyItemEnrichmentResult(
	ListedItem PrimaryItem,
	IReadOnlyCollection<ListedItem> AdditionalItems);
