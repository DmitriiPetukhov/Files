// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
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
		IntPtr buffer,
		int bufferLength,
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
		IntPtr buffer,
		int bufferLength,
		int notifyFilters,
		ref OVERLAPPED overlapped,
		out int errorCode)
	{
		var result = ReadDirectoryChangesW(
			watchHandle,
			(byte*)buffer,
			bufferLength,
			false,
			notifyFilters,
			null,
			ref overlapped,
			null);
		errorCode = result ? 0 : Marshal.GetLastWin32Error();
		return result;
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

internal enum Win32FolderChangeLifecycle
{
	Started,
	Canceled,
	Completed,
	Failed,
}

internal readonly record struct Win32FolderChangeDiagnostic(
	Win32FolderChangeLifecycle Lifecycle,
	Guid WatcherId,
	string PathIdentifier,
	TimeSpan Elapsed,
	bool WatchStarted,
	int ParsedNotificationCount,
	Exception? Exception);

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
	private readonly Action<Win32FolderChangeDiagnostic> reportDiagnostic;

	public Win32FolderChangeSource(string path, bool includeAttributes)
		: this(path, includeAttributes, new Win32FolderChangeNative(), null)
	{
	}

	internal Win32FolderChangeSource(
		string path,
		bool includeAttributes,
		IWin32FolderChangeNative native,
		Action<Win32FolderChangeDiagnostic>? reportDiagnostic = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentNullException.ThrowIfNull(native);

		this.path = path;
		this.includeAttributes = includeAttributes;
		this.native = native;
		this.reportDiagnostic = reportDiagnostic ?? LogDiagnostic;
	}

	public Task WatchAsync(
		Action<IReadOnlyCollection<Win32FolderChangeNotification>> publishBatch,
		CancellationToken cancellationToken,
		Action? onStarted = null)
	{
		ArgumentNullException.ThrowIfNull(publishBatch);

		return Task.Factory.StartNew(
			() => WatchCore(publishBatch, cancellationToken, onStarted),
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);
	}

	private void WatchCore(
		Action<IReadOnlyCollection<Win32FolderChangeNotification>> publishBatch,
		CancellationToken cancellationToken,
		Action? onStarted)
	{
		if (cancellationToken.IsCancellationRequested)
			return;

		var watcherId = Guid.NewGuid();
		var stopwatch = Stopwatch.StartNew();
		var parsedNotificationCount = 0;
		var watchStarted = false;
		var failureReported = false;
		var diagnosticReported = false;
		var watchHandle = IntPtr.Zero;

		var cancellationRequested = 0;
		void CancelPendingIo()
		{
			if (Interlocked.Exchange(ref cancellationRequested, 1) == 0)
				native.CancelIoEx(watchHandle);
		}
		IntPtr bufferPointer = IntPtr.Zero;
		SafeFileHandle? eventHandle = null;
		var overlapped = new OVERLAPPED();
		var readPending = false;

		try
		{
			watchHandle = native.CreateWatchHandle(path);

			if (watchHandle == IntPtr.Zero || watchHandle.ToInt64() == INVALID_HANDLE_VALUE)
			{
				stopwatch.Stop();
				reportDiagnostic(new(
					Win32FolderChangeLifecycle.Completed,
					watcherId,
					LogPathHelper.GetPathIdentifier(path),
					stopwatch.Elapsed,
					false,
					0,
					null));
				diagnosticReported = true;
				return;
			}

			var notifyFilters = FILE_NOTIFY_CHANGE_DIR_NAME |
				FILE_NOTIFY_CHANGE_FILE_NAME |
				FILE_NOTIFY_CHANGE_LAST_WRITE |
				FILE_NOTIFY_CHANGE_SIZE;

			if (includeAttributes)
				notifyFilters |= FILE_NOTIFY_CHANGE_ATTRIBUTES;

			var buffer = new byte[BufferSize];
			bufferPointer = Marshal.AllocHGlobal(BufferSize);
			eventHandle = native.CreateEvent();
			overlapped.hEvent = eventHandle!.DangerousGetHandle();

			using var cancellationRegistration = cancellationToken.Register(CancelPendingIo);
			if (cancellationToken.IsCancellationRequested)
				return;

			watchStarted = true;
			reportDiagnostic(new(
				Win32FolderChangeLifecycle.Started,
				watcherId,
				LogPathHelper.GetPathIdentifier(path),
				stopwatch.Elapsed,
				true,
				0,
				null));
			onStarted?.Invoke();

			while (!cancellationToken.IsCancellationRequested)
			{
				if (!native.ReadDirectoryChanges(
					watchHandle,
					bufferPointer,
					BufferSize,
					notifyFilters,
					ref overlapped,
					out var readErrorCode) && readErrorCode != ErrorIoPending)
				{
					throw new Win32Exception(readErrorCode);
				}
				readPending = true;

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
				readPending = false;

				if (bytesTransferred == 0)
					continue;

				Marshal.Copy(bufferPointer, buffer, 0, checked((int)bytesTransferred));

				var notifications = Win32FolderChangeParser.Parse(
					buffer.AsSpan(0, checked((int)bytesTransferred)),
					path);
				parsedNotificationCount += notifications.Count;

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
			failureReported = true;
			reportDiagnostic(new(
				Win32FolderChangeLifecycle.Failed,
				watcherId,
				LogPathHelper.GetPathIdentifier(path),
				stopwatch.Elapsed,
				watchStarted,
				parsedNotificationCount,
				ex));
			throw;
		}
		finally
		{
			if (watchHandle != IntPtr.Zero && watchHandle.ToInt64() != INVALID_HANDLE_VALUE)
			{
				CancelPendingIo();
				if (readPending)
					native.GetOverlappedResult(watchHandle, ref overlapped, out _, true, out _);
			}
			eventHandle?.Dispose();
			if (watchHandle != IntPtr.Zero && watchHandle.ToInt64() != INVALID_HANDLE_VALUE)
				native.CloseHandle(watchHandle);
			if (bufferPointer != IntPtr.Zero)
				Marshal.FreeHGlobal(bufferPointer);
			stopwatch.Stop();
			if (!failureReported && !diagnosticReported)
				reportDiagnostic(new(
					cancellationToken.IsCancellationRequested ? Win32FolderChangeLifecycle.Canceled : Win32FolderChangeLifecycle.Completed,
					watcherId,
					LogPathHelper.GetPathIdentifier(path),
					stopwatch.Elapsed,
					watchStarted,
					parsedNotificationCount,
					null));
		}
	}

	private static void LogDiagnostic(Win32FolderChangeDiagnostic diagnostic)
	{
		var message = "Win32 folder change source {Lifecycle} for {PathIdentifier}; watcher {WatcherId}, elapsed {ElapsedMs} ms, parsed {ParsedNotificationCount} notifications.";
		if (diagnostic.Lifecycle == Win32FolderChangeLifecycle.Failed)
		{
			App.Logger.LogWarning(
				diagnostic.Exception,
				message,
				diagnostic.Lifecycle,
				diagnostic.PathIdentifier,
				diagnostic.WatcherId,
				diagnostic.Elapsed.TotalMilliseconds,
				diagnostic.ParsedNotificationCount);
			return;
		}

		App.Logger.LogInformation(
			message,
			diagnostic.Lifecycle,
			diagnostic.PathIdentifier,
			diagnostic.WatcherId,
			diagnostic.Elapsed.TotalMilliseconds,
			diagnostic.ParsedNotificationCount);
	}
}
