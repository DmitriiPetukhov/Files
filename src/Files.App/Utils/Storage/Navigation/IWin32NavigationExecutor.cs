// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Items;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Projections;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Executes one Win32 navigation while owning its provider resources.</summary>
internal interface IWin32NavigationExecutor
{
	/// <summary>Executes progressive native navigation through the worker state stream.</summary>
	/// <param name="path">Folder path to enumerate.</param>
	/// <param name="publicationCoordinator">Coordinator that owns provider-neutral state.</param>
	/// <param name="projection">Navigation-scoped compatibility projection.</param>
	/// <param name="stateReader">Worker state reader and UI coalescing boundary.</param>
	/// <param name="initializeCurrentFolder">Callback invoked while the scope is owned.</param>
	/// <param name="cancellationToken">Token that cancels navigation.</param>
	/// <returns>The execution outcome for the caller's UI flow.</returns>
	Task<Win32NavigationExecutionResult> ExecuteAsync(
		string path,
		IFolderPublicationCoordinator publicationCoordinator,
		FolderItemListedItemProjection projection,
		FolderPublicationStateReader stateReader,
		Action<FolderItemMetadata?> initializeCurrentFolder,
		CancellationToken cancellationToken);

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
