// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Exposes the components owned by one navigation operation.</summary>
internal interface INavigationScope : IAsyncDisposable
{
	/// <summary>Gets the enumeration source owned by this scope.</summary>
	IFolderEnumerationSource EnumerationSource { get; }
}
