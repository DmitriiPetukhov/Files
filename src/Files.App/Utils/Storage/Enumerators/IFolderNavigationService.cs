// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Owns the lifecycle of the active folder navigation.
/// </summary>
internal interface IFolderNavigationService
{
	/// <summary>
	/// Starts navigation to the specified folder.
	/// </summary>
	/// <param name="path">The source-independent folder path.</param>
	/// <param name="cancellationToken">The token for the navigation request.</param>
	Task OpenAsync(string path, CancellationToken cancellationToken);

	/// <summary>
	/// Cancels the active navigation and prevents its late results from reaching the UI.
	/// </summary>
	Task CancelCurrentAsync();
}
