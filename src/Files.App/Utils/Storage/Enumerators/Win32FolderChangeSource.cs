// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using static Files.App.Helpers.Win32PInvoke;

namespace Files.App.Utils.Storage;

internal interface IWin32FolderChangeNative
{
	IntPtr CreateWatchHandle(string path);
	SafeFileHandle CreateEvent();
	bool ReadDirectoryChanges(
		IntPtr watchHandle,
		byte[] buffer,
		int notifyFilters,
		ref OVERLAPPED overlapped,
		out int errorCode);
	uint WaitForSingleObjectEx(IntPtr eventHandle, uint timeout, bool alertable, out int errorCode);
	bool GetOverlappedResult(
		IntPtr watchHandle,
		ref OVERLAPPED overlapped,
		out uint bytesTransferred,
		bool wait,
		out int errorCode);
	void CancelIoEx(IntPtr watchHandle);
	void CloseHandle(IntPtr watchHandle);
}

internal sealed class Win32FolderChangeNative : IWin32FolderChangeNative
{
	public IntPtr CreateWatchHandle(string path)
		=> CreateFileFromApp(
			path,
			1,
			FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
			IntPtr.Zero,
			OPEN_EXISTING,
			(uint)File_Attributes.BackupSemantics | (uint)File_Attributes.Overlapped,
			IntPtr.Zero);

	public SafeFileHandle CreateEvent()
		=> PInvoke.CreateEvent(null, false, false, null);

	public unsafe bool ReadDirectoryChanges(
		IntPtr watchHandle,
		byte[] buffer,
		int notifyFilters,
		ref OVERLAPPED overlapped,
		out int errorCode)
	{
		fixed (byte* bufferPointer = buffer)
		{
			var result = ReadDirectoryChangesW(
				watchHandle,
				bufferPointer,
				buffer.Length,
				false,
				notifyFilters,
				null,
				ref overlapped,
				null);
			errorCode = result ? 0 : Marshal.GetLastWin32Error();
			return result;
		}
	}

	public uint WaitForSingleObjectEx(IntPtr eventHandle, uint timeout, bool alertable, out int errorCode)
	{
		var result = Files.App.Helpers.Win32PInvoke.WaitForSingleObjectEx(eventHandle, timeout, alertable);
		errorCode = result == 0xFFFFFFFF ? Marshal.GetLastWin32Error() : 0;
		return result;
	}

	public bool GetOverlappedResult(
		IntPtr watchHandle,
		ref OVERLAPPED overlapped,
		out uint bytesTransferred,
		bool wait,
		out int errorCode)
	{
		var result = Files.App.Helpers.Win32PInvoke.GetOverlappedResult(
			watchHandle,
			ref overlapped,
			out bytesTransferred,
			wait);
		errorCode = result ? 0 : Marshal.GetLastWin32Error();
		return result;
	}

	public void CancelIoEx(IntPtr watchHandle)
		=> Files.App.Helpers.Win32PInvoke.CancelIoEx(watchHandle, IntPtr.Zero);

	public void CloseHandle(IntPtr watchHandle)
		=> Files.App.Helpers.Win32PInvoke.CloseHandle(watchHandle);
}

/// <summary>
/// Acquires ordered native folder-change notifications without interpreting them for the UI.
/// </summary>
internal sealed class Win32FolderChangeSource
{
	private const int BufferSize = 4096;
	private const uint Infinite = 0xFFFFFFFF;
	private const int ErrorIoPending = 997;

	private readonly string path;
	private readonly bool includeAttributes;
	private readonly IWin32FolderChangeNative native;

	public Win32FolderChangeSource(string path, bool includeAttributes)
		: this(path, includeAttributes, new Win32FolderChangeNative())
	{
	}

	internal Win32FolderChangeSource(string path, bool includeAttributes, IWin32FolderChangeNative native)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentNullException.ThrowIfNull(native);

		this.path = path;
		this.includeAttributes = includeAttributes;
		this.native = native;
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

		var watchHandle = native.CreateWatchHandle(path);

		if (watchHandle == IntPtr.Zero || watchHandle.ToInt64() == INVALID_HANDLE_VALUE)
			return;

		var cancellationRequested = 0;
		void CancelPendingIo()
		{
			if (Interlocked.Exchange(ref cancellationRequested, 1) == 0)
				native.CancelIoEx(watchHandle);
		}

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
			using var eventHandle = native.CreateEvent();
			overlapped.hEvent = eventHandle.DangerousGetHandle();

			using var cancellationRegistration = cancellationToken.Register(CancelPendingIo);

			while (!cancellationToken.IsCancellationRequested)
			{
				if (!native.ReadDirectoryChanges(
					watchHandle,
					buffer,
					notifyFilters,
					ref overlapped,
					out var readErrorCode) && readErrorCode != ErrorIoPending)
				{
					throw new Win32Exception(readErrorCode);
				}

				var waitResult = native.WaitForSingleObjectEx(overlapped.hEvent, Infinite, true, out var waitErrorCode);
				if (waitResult == Infinite)
					throw new Win32Exception(waitErrorCode);

				if (cancellationToken.IsCancellationRequested)
					break;

				if (!native.GetOverlappedResult(
					watchHandle,
					ref overlapped,
					out var bytesTransferred,
					false,
					out var resultErrorCode))
				{
					throw new Win32Exception(resultErrorCode);
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
			CancelPendingIo();
			native.CloseHandle(watchHandle);
		}
	}
}
