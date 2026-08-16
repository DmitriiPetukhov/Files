// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading.Tasks;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Opens a Win32 folder and returns its source-owned enumeration result.</summary>
internal interface IWin32FolderOpener
{
	/// <summary>Starts opening the specified folder.</summary>
	Task<Win32FolderEnumerationOpenResult> OpenAsync(string path);
}
