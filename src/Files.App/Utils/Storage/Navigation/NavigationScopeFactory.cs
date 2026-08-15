// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Composes the navigation-scoped components that are currently implemented.</summary>
internal sealed class NavigationScopeFactory
{
	private const string Win32ProviderId = "win32";

	/// <summary>Creates a navigation scope factory with the built-in provider implementations.</summary>
	public NavigationScopeFactory() { }

	/// <summary>Opens a provider source and creates the scope that owns it.</summary>
	/// <param name="folder">Folder identity used to select the provider.</param>
	/// <param name="cancellationToken">Token that cancels the open attempt.</param>
	/// <returns>A provider-neutral scope-open result.</returns>
	public Task<NavigationScopeOpenResult> TryCreateAsync(
		FolderReference folder,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folder);

		if (!string.Equals(folder.ProviderId, Win32ProviderId, StringComparison.Ordinal))
			throw new ArgumentException("The folder provider is not supported.", nameof(folder));

		return Win32NavigationScopeProvider.TryCreateAsync(folder.OpaqueId, cancellationToken);
	}
}
