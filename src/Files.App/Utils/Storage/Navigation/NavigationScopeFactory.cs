// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Composes the navigation-scoped components that are currently implemented.</summary>
internal sealed class NavigationScopeFactory
{
	private const string Win32ProviderId = "win32";

	/// <summary>Creates a navigation scope for a supported folder.</summary>
	/// <param name="folder">Folder identity used to select the source.</param>
	/// <param name="handle">Already-opened Win32 search handle.</param>
	/// <param name="firstFindData">First entry returned with the search handle.</param>
	/// <returns>A scope containing the source bound to the requested folder.</returns>
	public INavigationScope Create(
		FolderReference folder,
		IntPtr handle,
		WIN32_FIND_DATA firstFindData)
		=> new NavigationScope(CreateEnumerationSource(folder, handle, firstFindData));

	/// <summary>Creates the enumeration source for the current provider set.</summary>
	/// <param name="folder">Folder identity used to select the source.</param>
	/// <param name="handle">Already-opened Win32 search handle.</param>
	/// <param name="firstFindData">First entry returned with the search handle.</param>
	/// <returns>The enumeration source bound to the requested folder.</returns>
	private static IFolderEnumerationSource CreateEnumerationSource(
		FolderReference folder,
		IntPtr handle,
		WIN32_FIND_DATA firstFindData)
	{
		ArgumentNullException.ThrowIfNull(folder);

		if (!string.Equals(folder.ProviderId, Win32ProviderId, StringComparison.Ordinal))
			throw new ArgumentException("The folder provider is not supported.", nameof(folder));

		return new Win32FolderEnumerationSource(folder.OpaqueId, handle, firstFindData);
	}
}
