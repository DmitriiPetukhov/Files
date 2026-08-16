// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Contracts;

/// <summary>Describes the universal kind of an enumerated item.</summary>
internal enum FolderItemKind
{
	/// <summary>Regular file item.</summary>
	File = 0,
	/// <summary>Directory item.</summary>
	Folder = 1,
	/// <summary>Item that does not fit another universal kind.</summary>
	Other = 2,
}
