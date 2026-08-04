// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using static Files.App.Helpers.Win32PInvoke;

namespace Files.App.Utils.Storage;

/// <summary>
/// Acquires ordered native folder-change notifications without interpreting them for the UI.
/// </summary>
internal sealed class Win32FolderChangeSource
{
	private const int BufferSize = 4096;
	private const uint Infinite = 0xFFFFFFFF;

	private readonly string path;
	private readonly bool includeAttributes;

	public Win32FolderChangeSource(string path, bool includeAttributes)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);

		this.path = path;
		this.includeAttributes = includeAttributes;
	}

	public Task WatchAsync(
		Action<IReadOnlyCollection<Win32FolderChangeNotification>> publishBatch,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatch);

		return Task.Factory.StartNew(
			() => WatchCore(publishBatch, cancellationToken),
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);
	}

	private void WatchCore(
		Action<IReadOnlyCollection<Win32FolderChangeNotification>> publishBatch,
		CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
			return;

		var watchHandle = CreateFileFromApp(
			path,
			1,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
			IntPtr.Zero,
			OPEN_EXISTING,
			(uint)File_Attributes.BackupSemantics | (uint)File_Attributes.Overlapped,
			IntPtr.Zero);

		if (watchHandle == IntPtr.Zero || watchHandle.ToInt64() == INVALID_HANDLE_VALUE)
			return;

		try
		{
			var notifyFilters = FILE_NOTIFY_CHANGE_DIR_NAME |
				FILE_NOTIFY_CHANGE_FILE_NAME |
				FILE_NOTIFY_CHANGE_LAST_WRITE |
				FILE_NOTIFY_CHANGE_SIZE;

			if (includeAttributes)
				notifyFilters |= FILE_NOTIFY_CHANGE_ATTRIBUTES;

			var buffer = new byte[BufferSize];
			var overlapped = new OVERLAPPED();
			using var eventHandle = PInvoke.CreateEvent(null, false, false, null);
			overlapped.hEvent = eventHandle.DangerousGetHandle();

			using var cancellationRegistration = cancellationToken.Register(() => CancelIoEx(watchHandle, IntPtr.Zero));

			while (!cancellationToken.IsCancellationRequested)
			{
				unsafe
				{
					fixed (byte* bufferPointer = buffer)
					{
						if (!ReadDirectoryChangesW(
							watchHandle,
							bufferPointer,
							buffer.Length,
							false,
							notifyFilters,
							null,
							ref overlapped,
							null))
						{
							throw new Win32Exception(Marshal.GetLastWin32Error());
						}

						var waitResult = WaitForSingleObjectEx(overlapped.hEvent, Infinite, true);
						if (waitResult == Infinite)
							throw new Win32Exception(Marshal.GetLastWin32Error());

						if (cancellationToken.IsCancellationRequested)
							break;

						if (!GetOverlappedResult(
							watchHandle,
							ref overlapped,
							out var bytesTransferred,
							false))
						{
							throw new Win32Exception(Marshal.GetLastWin32Error());
						}

						if (bytesTransferred == 0)
							continue;

						var notifications = Win32FolderChangeParser.Parse(
							buffer.AsSpan(0, checked((int)bytesTransferred)),
							path);

						if (notifications.Count > 0 && !cancellationToken.IsCancellationRequested)
							publishBatch(notifications);
					}
				}
			}
		}
		catch (Win32Exception) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			App.Logger.LogWarning(
				ex,
				"Win32 folder change source failed for {PathIdentifier}.",
				LogPathHelper.GetPathIdentifier(path));
			throw;
		}
		finally
		{
			CancelIoEx(watchHandle, IntPtr.Zero);
			CloseHandle(watchHandle);
		}
	}
}
