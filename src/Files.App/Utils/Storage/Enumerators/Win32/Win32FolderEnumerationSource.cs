// Copyright (c) Files Community
// Licensed under the MIT License.

using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;
using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage.Enumerators.Win32;

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
	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);

		try
		{
			return await Win32StorageEnumerator.ListEntries(
				path,
				handle,
				findData,
				cancellationToken,
				-1,
				intermediateAction: publishBatchAsync);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			App.Logger.LogWarning(
				ex,
				"Win32 folder enumeration failed. Path={Path} ErrorType={ErrorType} NativeErrorCode={NativeErrorCode}",
				LogPathHelper.GetPathIdentifier(path),
				ex.GetType().Name,
				(ex as Win32Exception)?.NativeErrorCode);

			throw;
		}
	}
}
