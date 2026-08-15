// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Describes the provider-bound result of opening a Win32 folder enumeration.</summary>
internal sealed record Win32FolderEnumerationOpenResult(
	Win32FolderEnumerationOpenStatus Status,
	Win32FolderEnumerationSource? Source,
	FolderItemMetadata? InitialMetadata,
	int NativeErrorCode);

/// <summary>Classifies native Win32 folder-open outcomes before UI fallback handling.</summary>
internal enum Win32FolderEnumerationOpenStatus
{
	Opened,
	ZeroHandle,
	InvalidHandle,
	Canceled,
}
