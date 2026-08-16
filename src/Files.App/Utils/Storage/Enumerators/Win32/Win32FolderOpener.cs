// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading.Tasks;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Starts non-cancelable native Win32 folder opens on a worker thread.</summary>
internal sealed class Win32FolderOpener : IWin32FolderOpener
{
	/// <inheritdoc />
	public Task<Win32FolderEnumerationOpenResult> OpenAsync(string path)
		=> Task.Run(() => Win32FolderEnumerationSource.Open(path));
}
