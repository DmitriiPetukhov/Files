// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Navigation;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Navigation;

/// <summary>Returns one scripted result from a navigation scope factory.</summary>
internal sealed class ScriptedNavigationScopeFactory : INavigationScopeFactory
{
	private readonly NavigationScopeOpenResult result;

	public ScriptedNavigationScopeFactory(NavigationScopeOpenResult result)
	{
		this.result = result ?? throw new ArgumentNullException(nameof(result));
	}

	/// <inheritdoc />
	public Task<NavigationScopeOpenResult> TryCreateAsync(
		FolderReference folder,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(result);
}
