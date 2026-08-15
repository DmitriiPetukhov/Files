// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Files.App.Helpers;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Adapts native Win32 folder enumeration to the provider-neutral source contract.</summary>
internal sealed class Win32FolderEnumerationSource : IFolderEnumerationSource
{
	private const string ProviderId = "win32";
	private const int BatchSize = 32;
	private static readonly Win32FolderOpenOperation openOperation =
		new(new Win32FolderOpener(), LogFailure);

	private readonly string path;
	private readonly IWin32FindHandle findHandle;
	private readonly WIN32_FIND_DATA firstFindData;
	private readonly Func<string, (IWin32FindHandle Handle, WIN32_FIND_DATA FindData)?> resolveLookup;
	private readonly Func<string, WIN32_FIND_DATA, FolderItem?> materialize;
	private int isDisposed;
	private int isHandleDisposed;

	public Win32FolderEnumerationSource(string path, IntPtr handle, WIN32_FIND_DATA firstFindData)
		: this(path, new Win32FindHandle(handle), firstFindData)
	{
	}

	internal Win32FolderEnumerationSource(
		string path,
		IWin32FindHandle findHandle,
		WIN32_FIND_DATA firstFindData,
		Func<string, (IWin32FindHandle Handle, WIN32_FIND_DATA FindData)?>? resolveLookup = null,
		Func<string, WIN32_FIND_DATA, FolderItem?>? materialize = null)
	{
		this.path = string.IsNullOrWhiteSpace(path)
			? throw new ArgumentException("A folder path is required.", nameof(path))
			: Path.GetFullPath(path);
		this.findHandle = findHandle ?? throw new ArgumentNullException(nameof(findHandle));
		this.firstFindData = firstFindData;
		this.resolveLookup = resolveLookup ?? OpenForResolution;
		this.materialize = materialize ?? CreateFolderItem;
	}

	/// <summary>Creates a source for a folder opened through the Win32 search API.</summary>
	internal static Win32FolderEnumerationSource TryCreate(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var openResult = Open(path);
		if (openResult.Source is null)
		{
			var exception = new Win32Exception(openResult.NativeErrorCode);
			LogFailure(path, exception);
			throw exception;
		}

		return openResult.Source;
	}

	/// <summary>Opens a Win32 folder source while keeping handle ownership at the source boundary.</summary>
	internal static Task<Win32FolderEnumerationOpenResult> TryOpenAsync(
		string path,
		CancellationToken cancellationToken)
		=> openOperation.TryOpenAsync(path, cancellationToken);

	/// <inheritdoc />
	public IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		return EnumerateItemsAsync(cancellationToken);
	}

	/// <inheritdoc />
	public ValueTask<FolderItem?> ResolveAsync(FolderItemKey itemKey, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		var itemPath = ValidateItemKey(itemKey);
		try
		{
			var lookup = resolveLookup(itemPath);
			if (lookup is null)
				return ValueTask.FromResult<FolderItem?>(null);

			using (lookup.Value.Handle)
			{
				var parentPath = Path.GetDirectoryName(itemPath) ?? path;
				return ValueTask.FromResult(materialize(parentPath, lookup.Value.FindData));
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LogFailure(ex);
			throw;
		}
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		Interlocked.Exchange(ref isDisposed, 1);
		DisposeFindHandle();
		return ValueTask.CompletedTask;
	}

	private async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateItemsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var pendingBatch = new List<FolderItem>(BatchSize);
		long sequenceNumber = 0;
		var findData = firstFindData;

		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var item = MaterializeItem(findData);
				if (item is not null)
				{
					pendingBatch.Add(item);
					if (pendingBatch.Count == BatchSize)
					{
						yield return new FolderEnumerationBatch<FolderItem>(pendingBatch, sequenceNumber++);
						pendingBatch.Clear();
					}
				}

				if (!TryMoveNext(out findData))
					break;
			}

			if (pendingBatch.Count > 0)
				yield return new FolderEnumerationBatch<FolderItem>(pendingBatch, sequenceNumber);
		}
		finally
		{
			DisposeFindHandle();
		}
	}

	private FolderItem? MaterializeItem(WIN32_FIND_DATA findData)
	{
		try
		{
			return materialize(path, findData);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LogFailure(ex);
			throw;
		}
	}

	private bool TryMoveNext(out WIN32_FIND_DATA findData)
	{
		try
		{
			return findHandle.MoveNext(out findData);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LogFailure(ex);
			throw;
		}
	}

	private static FolderItem? CreateFolderItem(string path, WIN32_FIND_DATA findData)
	{
		if (findData.cFileName is "." or "..")
			return null;

		var itemPath = Path.GetFullPath(Path.Combine(path, findData.cFileName));
		var isFolder = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != 0;
		var metadata = new FolderItemMetadata(
			isFolder ? null : findData.GetSize(),
			null,
			null,
			((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Hidden));

		return new FolderItem(
			new FolderItemKey(ProviderId, itemPath),
			findData.cFileName,
			isFolder ? FolderItemKind.Folder : FolderItemKind.File,
			metadata,
			new Win32FolderItemData(findData));
	}

	private (IWin32FindHandle Handle, WIN32_FIND_DATA FindData)? OpenForResolution(string itemPath)
	{
		if (Win32FindHandle.TryOpen(itemPath, out var handle, out var findData, out var nativeErrorCode, out _))
			return (handle!, findData);

		if ((WIN32_ERROR)nativeErrorCode is WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND)
			return null;

		throw new Win32Exception(nativeErrorCode);
	}

	private string ValidateItemKey(FolderItemKey itemKey)
	{
		if (!string.Equals(itemKey.ProviderId, ProviderId, StringComparison.Ordinal))
			throw new ArgumentException("The item key belongs to another provider.", nameof(itemKey));

		var itemPath = Path.GetFullPath(itemKey.OpaqueId);
		var parentPath = Path.GetDirectoryName(itemPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var sourcePath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!string.Equals(parentPath, sourcePath, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("The item key belongs to another folder.", nameof(itemKey));

		return itemPath;
	}

	internal static Win32FolderEnumerationOpenResult Open(string path)
	{
		if (Win32FindHandle.TryOpen(
			Path.Combine(path, "*.*"),
			out var findHandle,
			out var firstFindData,
			out var nativeErrorCode,
			out var openStatus))
		{
			var source = new Win32FolderEnumerationSource(path, findHandle!, firstFindData);
			var initialMetadata = CreateInitialMetadata(firstFindData);

			return new Win32FolderEnumerationOpenResult(
				Win32FolderEnumerationOpenStatus.Opened,
				source,
				initialMetadata,
				nativeErrorCode);
		}

		return new Win32FolderEnumerationOpenResult(
			openStatus switch
			{
				Win32FindHandleOpenStatus.ZeroHandle => Win32FolderEnumerationOpenStatus.ZeroHandle,
				Win32FindHandleOpenStatus.InvalidHandle => Win32FolderEnumerationOpenStatus.InvalidHandle,
				_ => throw new InvalidOperationException("The Win32 find handle open status is invalid."),
			},
			null,
			null,
			nativeErrorCode);
	}

	private static FolderItemMetadata CreateInitialMetadata(WIN32_FIND_DATA findData)
		=> new(
			((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Directory) ? null : findData.GetSize(),
			ConvertFileTime(findData.ftCreationTime),
			ConvertFileTime(findData.ftLastWriteTime),
			((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Hidden));

	private static DateTimeOffset? ConvertFileTime(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
	{
		if (fileTime.dwHighDateTime == 0 && fileTime.dwLowDateTime == 0)
			return null;

		try
		{
			var value = ((long)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
			return DateTimeOffset.FromFileTime(value);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref isDisposed) != 0,
			typeof(Win32FolderEnumerationSource));
	}

	private void DisposeFindHandle()
	{
		if (Interlocked.Exchange(ref isHandleDisposed, 1) == 0)
			findHandle.Dispose();
	}

	private void LogFailure(Exception exception)
		=> LogFailure(path, exception);

	private static void LogFailure(string path, Exception exception)
	{
		App.Logger.LogWarning(
			exception,
			"Win32 folder enumeration failed. Path={Path} ErrorType={ErrorType} NativeErrorCode={NativeErrorCode}",
			LogPathHelper.GetPathIdentifier(path),
			exception.GetType().Name,
			(exception as Win32Exception)?.NativeErrorCode);
	}
}
