// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage;

/// <summary>
/// Adapts native Win32 folder enumeration to the provider-neutral source contract.
/// </summary>
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

	/// <inheritdoc />
	public async Task<ImmutableArray<ListedItem>> EnumerateAsync(
		Func<ImmutableArray<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);

		var acceptedItems = await Win32StorageEnumerator.ListEntries(
			path,
			handle,
			findData,
			cancellationToken,
			-1,
			intermediateAction: intermediateList =>
				publishBatchAsync(intermediateList.ToImmutableArray()));

		return acceptedItems.ToImmutableArray();
	}
}
