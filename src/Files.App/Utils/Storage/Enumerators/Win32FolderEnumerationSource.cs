// Copyright (c) Files Community
// Licensed under the MIT License.

using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage;

internal sealed class Win32FolderEnumerationSource : IFolderEnumerationSource<ListedItem>
{
	private readonly string path;
	private readonly IntPtr handle;
	private readonly WIN32_FIND_DATA findData;

	public Win32FolderEnumerationSource(string path, IntPtr handle, WIN32_FIND_DATA findData)
	{
		this.path = path;
		this.handle = handle;
		this.findData = findData;
	}

	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);

		return await Win32StorageEnumerator.ListEntries(
			path,
			handle,
			findData,
			cancellationToken,
			-1,
			intermediateAction: async intermediateList => await publishBatchAsync(intermediateList));
	}
}
