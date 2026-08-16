// Copyright (c) Files Community
// Licensed under the MIT License.

using WIN32_FIND_DATA = Files.App.Helpers.Win32PInvoke.WIN32_FIND_DATA;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Advances and releases one native Win32 folder search.</summary>
internal interface IWin32FindHandle : IDisposable
{
	/// <summary>Reads the next native folder entry.</summary>
	bool MoveNext(out WIN32_FIND_DATA findData);
}
