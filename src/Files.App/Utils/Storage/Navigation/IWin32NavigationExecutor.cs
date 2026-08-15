// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Items;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Executes one Win32 navigation while owning its provider resources.</summary>
internal interface IWin32NavigationExecutor
{
	/// <summary>Opens, enumerates, and publishes one Win32 folder.</summary>
	/// <param name="path">Folder path to enumerate.</param>
	/// <param name="publicationCoordinator">Coordinator that receives projected items.</param>
	/// <param name="initializeCurrentFolder">Callback invoked while the scope is owned.</param>
	/// <param name="cancellationToken">Token that cancels navigation.</param>
	/// <returns>The execution outcome for the caller's UI flow.</returns>
	Task<Win32NavigationExecutionResult> ExecuteAsync(
		string path,
		IFolderPublicationCoordinator<ListedItem> publicationCoordinator,
		Action<FolderItemMetadata?> initializeCurrentFolder,
		CancellationToken cancellationToken);
}
