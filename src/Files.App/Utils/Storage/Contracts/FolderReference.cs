// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Contracts;

/// <summary>Identifies the folder bound to a navigation scope.</summary>
internal sealed record FolderReference
{
	/// <summary>Creates a folder reference in a provider identity namespace.</summary>
	/// <param name="providerId">Provider that owns the folder.</param>
	/// <param name="opaqueId">Provider-defined folder identifier.</param>
	public FolderReference(string providerId, string opaqueId)
	{
		ProviderId = string.IsNullOrWhiteSpace(providerId)
			? throw new ArgumentException("ProviderId is required.", nameof(providerId))
			: providerId;
		OpaqueId = string.IsNullOrWhiteSpace(opaqueId)
			? throw new ArgumentException("OpaqueId is required.", nameof(opaqueId))
			: opaqueId;
	}

	/// <summary>Gets the provider that owns the folder identity.</summary>
	public string ProviderId { get; }

	/// <summary>Gets the provider-defined folder identifier.</summary>
	public string OpaqueId { get; }
}
