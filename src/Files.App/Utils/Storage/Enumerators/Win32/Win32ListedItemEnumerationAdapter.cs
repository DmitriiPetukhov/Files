// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.IO;
using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Enumerators;
using Microsoft.Extensions.Logging;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Bridges the legacy ListedItem callback path while the UI projection is migrated.</summary>
internal sealed class Win32ListedItemEnumerationAdapter(
	string folderPath,
	IntPtr handle,
	WIN32_FIND_DATA firstFindData) : Files.App.Utils.Storage.IFolderEnumerationSource<ListedItem>
{
	private const int NoCountLimit = -1;

	private readonly string path = string.IsNullOrWhiteSpace(folderPath)
		? throw new ArgumentException("A folder path is required.", nameof(folderPath))
		: Path.GetFullPath(folderPath);
	private readonly IWin32FindHandle findHandle = new Win32FindHandle(handle);
	private int isDisposed;

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<ListedItem>> EnumerateAsync(
		Func<IReadOnlyCollection<ListedItem>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);
		ThrowIfDisposed();

		try
		{
			return await Win32StorageEnumerator.ListEntries(
				path,
				findHandle,
				firstFindData,
				NoCountLimit,
				intermediateAction: publishBatchAsync,
				cancellationToken: cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			App.Logger.LogWarning(
				ex,
				"Legacy Win32 folder enumeration failed. Path={Path} ErrorType={ErrorType} NativeErrorCode={NativeErrorCode}",
				LogPathHelper.GetPathIdentifier(path),
				ex.GetType().Name,
				(ex as Win32Exception)?.NativeErrorCode);
			throw;
		}
		finally
		{
			DisposeFindHandle();
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) != 0,
			typeof(Win32ListedItemEnumerationAdapter));
	}

	private void DisposeFindHandle()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) == 0)
			findHandle.Dispose();
	}
}
