// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Creates provider-neutral scopes for folder navigation.</summary>
internal interface INavigationScopeFactory
{
	/// <summary>Opens the requested folder and returns its navigation scope.</summary>
	/// <param name="folder">Folder identity to open.</param>
	/// <param name="cancellationToken">Token that cancels the open attempt.</param>
	/// <returns>The provider-neutral open result.</returns>
	Task<NavigationScopeOpenResult> TryCreateAsync(
		FolderReference folder,
		CancellationToken cancellationToken = default);
}
