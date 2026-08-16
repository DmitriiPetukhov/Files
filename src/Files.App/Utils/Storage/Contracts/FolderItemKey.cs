// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Contracts;

/// <summary>Identifies an item within a provider identity namespace.</summary>
/// <param name="ProviderId">Provider that owns the identity.</param>
/// <param name="OpaqueId">Provider-defined stable identifier.</param>
internal readonly record struct FolderItemKey(string ProviderId, string OpaqueId)
{
	/// <summary>Gets whether either identity component is missing.</summary>
	public bool IsEmpty
		=> string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(OpaqueId);
}
