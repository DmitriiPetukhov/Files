// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Resolves the repository state needed by Win32 item materialization.</summary>
internal interface IWin32GitStateResolver
{
	/// <summary>Determines whether a folder is a valid Git repository.</summary>
	/// <param name="path">Folder path to inspect.</param>
	/// <param name="cancellationToken">Token that cancels the lookup.</param>
	/// <returns><see langword="true"/> when the folder has a named repository head.</returns>
	Task<bool> IsRepositoryAsync(string path, CancellationToken cancellationToken = default);
}
