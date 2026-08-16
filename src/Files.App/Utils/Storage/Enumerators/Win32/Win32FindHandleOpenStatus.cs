// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Classifies the raw handle returned by the Win32 find API.</summary>
internal enum Win32FindHandleOpenStatus
{
	Opened,
	ZeroHandle,
	InvalidHandle,
}
