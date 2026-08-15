// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Navigation;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Navigation;

/// <summary>Provides scripted Git repository state for navigation tests.</summary>
internal sealed class ScriptedWin32GitStateResolver : IWin32GitStateResolver
{
	private readonly Func<string, bool> resolve;

	public ScriptedWin32GitStateResolver(Func<string, bool> resolve)
	{
		this.resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
	}

	/// <inheritdoc />
	public Task<bool> IsRepositoryAsync(string path, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(resolve(path));
	}
}
