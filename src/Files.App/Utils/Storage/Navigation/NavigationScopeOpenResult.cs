// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Describes the provider-neutral result of creating a navigation scope.</summary>
internal sealed record NavigationScopeOpenResult(
	NavigationScopeOpenStatus Status,
	INavigationScope? Scope,
	NavigationUnavailableReason? FailureReason,
	FolderItemMetadata? InitialMetadata);

/// <summary>Describes how navigation should proceed after a scope-open attempt.</summary>
internal enum NavigationScopeOpenStatus
{
	/// <summary>A scope was created and owns the opened enumeration source.</summary>
	Opened,
	/// <summary>The caller should use its configured fallback enumeration path.</summary>
	Fallback,
	/// <summary>The location cannot be opened and the caller should show an error.</summary>
	Unavailable,
	/// <summary>The open attempt was canceled by navigation or its timeout.</summary>
	Canceled,
}

/// <summary>Describes a provider-neutral location error that can be shown by the UI.</summary>
internal enum NavigationUnavailableReason
{
	/// <summary>The location denied access.</summary>
	AccessDenied,
	/// <summary>The location or folder was not found.</summary>
	NotFound,
	/// <summary>The location became unavailable, such as an unplugged drive.</summary>
	DriveUnplugged,
	/// <summary>The provider requires a password before the location can be opened.</summary>
	PasswordRequired,
	/// <summary>The provider reported an error without a more specific common mapping.</summary>
	Unknown,
}
