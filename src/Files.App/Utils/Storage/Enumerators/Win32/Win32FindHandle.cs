// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Files.App.Helpers;
using Windows.Win32.Foundation;
using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Owns a native Win32 folder search handle.</summary>
internal sealed partial class Win32FindHandle : IWin32FindHandle
{
	private IntPtr handle;

	internal Win32FindHandle(IntPtr handle)
	{
		if (handle == IntPtr.Zero || handle.ToInt64() == Win32PInvoke.INVALID_HANDLE_VALUE)
			throw new ArgumentException("A valid Win32 find handle is required.", nameof(handle));

		this.handle = handle;
	}

	/// <summary>Opens a Win32 search pattern and returns its first entry.</summary>
	internal static bool TryOpen(
		string searchPattern,
		out Win32FindHandle? findHandle,
		out WIN32_FIND_DATA firstFindData,
		out int nativeErrorCode)
	{
		var nativeHandle = Win32PInvoke.FindFirstFileExFromApp(
			searchPattern,
			Win32PInvoke.FINDEX_INFO_LEVELS.FindExInfoBasic,
			out firstFindData,
			Win32PInvoke.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
			IntPtr.Zero,
			Win32PInvoke.FIND_FIRST_EX_LARGE_FETCH);

		if (nativeHandle == IntPtr.Zero || nativeHandle.ToInt64() == Win32PInvoke.INVALID_HANDLE_VALUE)
		{
			findHandle = null;
			nativeErrorCode = Marshal.GetLastWin32Error();
			return false;
		}

		findHandle = new Win32FindHandle(nativeHandle);
		nativeErrorCode = 0;
		return true;
	}

	/// <inheritdoc />
	public bool MoveNext(out WIN32_FIND_DATA findData)
	{
		var currentHandle = handle;
		ObjectDisposedException.ThrowIf(currentHandle == IntPtr.Zero, typeof(Win32FindHandle));

		if (Win32PInvoke.FindNextFile(currentHandle, out findData))
			return true;

		var nativeError = (WIN32_ERROR)Marshal.GetLastWin32Error();
		if (nativeError != WIN32_ERROR.ERROR_NO_MORE_FILES)
			throw new Win32Exception((int)nativeError);

		findData = default;
		return false;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		var currentHandle = Interlocked.Exchange(ref handle, IntPtr.Zero);
		if (currentHandle != IntPtr.Zero)
			Win32PInvoke.FindClose(currentHandle);
	}
}
