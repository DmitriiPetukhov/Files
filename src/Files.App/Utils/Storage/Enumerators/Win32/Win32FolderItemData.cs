// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Helpers;
using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Stores the immutable native snapshot required by the legacy Win32 item materializer.</summary>
internal sealed record Win32FolderItemData(Win32PInvoke.WIN32_FIND_DATA FindData) : IProviderItemData;
