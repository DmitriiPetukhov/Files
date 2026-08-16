// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Git;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Resolves Git state through the application's existing Git helpers.</summary>
internal sealed class Win32GitStateResolver : IWin32GitStateResolver
{
	/// <inheritdoc />
	public async Task<bool> IsRepositoryAsync(
		string path,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!GitHelpers.IsRepositoryEx(path, out var repositoryPath))
			return false;

		cancellationToken.ThrowIfCancellationRequested();
		return !string.IsNullOrEmpty((await GitHelpers.GetRepositoryHead(repositoryPath))?.Name);
	}
}
