// Copyright (c) Files Community
// Licensed under the MIT License.

using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage;

internal static class FolderEnumerationSourceFactory
{
	public static IFolderEnumerationSource<ListedItem> Create(string path, IntPtr handle, WIN32_FIND_DATA findData)
		=> new Win32FolderEnumerationSource(path, handle, findData);
}
