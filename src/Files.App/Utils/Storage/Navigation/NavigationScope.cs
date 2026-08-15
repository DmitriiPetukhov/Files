// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Owns the components created for one navigation operation.</summary>
internal sealed class NavigationScope(IFolderEnumerationSource enumerationSource) : INavigationScope
{
	private int isDisposed;

	/// <inheritdoc />
	public IFolderEnumerationSource EnumerationSource { get; } =
		enumerationSource ?? throw new ArgumentNullException(nameof(enumerationSource));

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
			return ValueTask.CompletedTask;

		return EnumerationSource.DisposeAsync();
	}
}
