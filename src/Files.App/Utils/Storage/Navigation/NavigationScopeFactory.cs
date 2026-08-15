// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators.Win32;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32.Foundation;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Composes the navigation-scoped components that are currently implemented.</summary>
internal sealed class NavigationScopeFactory
{
	private const string Win32ProviderId = "win32";

	/// <summary>Opens a provider source and creates the scope that owns it.</summary>
	/// <param name="folder">Folder identity used to select the provider.</param>
	/// <param name="cancellationToken">Token that cancels the open attempt.</param>
	/// <returns>A provider-neutral scope-open result.</returns>
	public Task<NavigationScopeOpenResult> TryCreateAsync(
		FolderReference folder,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folder);

		if (!string.Equals(folder.ProviderId, Win32ProviderId, StringComparison.Ordinal))
			throw new ArgumentException("The folder provider is not supported.", nameof(folder));

		return TryCreateWin32Async(folder, cancellationToken);
	}

	private static async Task<NavigationScopeOpenResult> TryCreateWin32Async(
		FolderReference folder,
		CancellationToken cancellationToken)
	{
		var openResult = await Win32FolderEnumerationSource.TryOpenAsync(folder.OpaqueId, cancellationToken);
		return openResult.Status switch
		{
			Win32FolderEnumerationOpenStatus.Opened => new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Opened,
				new NavigationScope(openResult.Source!),
				null,
				openResult.InitialMetadata),
			Win32FolderEnumerationOpenStatus.ZeroHandle => new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Unavailable,
				null,
				NavigationUnavailableReason.DriveUnplugged,
				null),
			Win32FolderEnumerationOpenStatus.InvalidHandle => new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Fallback,
				null,
				MapFallbackReason(openResult.NativeErrorCode),
				null),
			Win32FolderEnumerationOpenStatus.Canceled => new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Canceled,
				null,
				null,
				null),
			_ => throw new InvalidOperationException("The Win32 source open status is invalid."),
		};
	}

	private static NavigationUnavailableReason? MapFallbackReason(int nativeErrorCode)
		=> (WIN32_ERROR)nativeErrorCode switch
		{
			WIN32_ERROR.ERROR_ACCESS_DENIED => NavigationUnavailableReason.AccessDenied,
			WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND => NavigationUnavailableReason.NotFound,
			_ => null,
		};
}
