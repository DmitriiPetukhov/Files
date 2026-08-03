// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Selects an enumeration source for a folder without exposing provider details to the UI.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal interface IFolderEnumerationSourceFactory<T>
{
	/// <summary>
	/// Determines whether this factory can enumerate the specified folder.
	/// </summary>
	/// <param name="path">The folder path to inspect.</param>
	/// <returns><see langword="true"/> when this factory supports the folder.</returns>
	bool CanHandle(string path);

	/// <summary>
	/// Creates a source for the specified folder.
	/// </summary>
	/// <param name="path">The folder path to enumerate.</param>
	/// <returns>A provider-specific source behind the common contract.</returns>
	IFolderEnumerationSource<T> Create(string path);
}
