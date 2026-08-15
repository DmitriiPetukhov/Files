using System;
using System.Collections.Generic;
using Files.App.Helpers;
using Files.App.Utils.Storage.Enumerators.Win32;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;

/// <summary>Provides deterministic Win32 find results for source tests.</summary>
internal sealed partial class ScriptedWin32FindHandle(
	IEnumerable<Win32PInvoke.WIN32_FIND_DATA> entries,
	Exception? moveNextException = null) : IWin32FindHandle
{
	private readonly Queue<Win32PInvoke.WIN32_FIND_DATA> entryQueue = new(entries);

	/// <summary>Gets the number of disposal calls received by the handle.</summary>
	public int DisposeCount { get; private set; }

	/// <summary>Returns the next scripted native entry.</summary>
	public bool MoveNext(out Win32PInvoke.WIN32_FIND_DATA findData)
	{
		if (entryQueue.Count > 0)
		{
			findData = entryQueue.Dequeue();
			return true;
		}

		if (moveNextException is not null)
			throw moveNextException;

		findData = default;
		return false;
	}

	/// <summary>Records disposal of the scripted native handle.</summary>
	public void Dispose() => DisposeCount++;
}
