// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Enumerators.Win32;
using Windows.Win32.Foundation;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Maps Win32 source-open outcomes into provider-neutral navigation results.</summary>
internal static class Win32NavigationScopeProvider
{
	/// <summary>Opens a Win32 navigation scope for the specified path.</summary>
	internal static async Task<NavigationScopeOpenResult> TryCreateAsync(
		string path,
		CancellationToken cancellationToken)
	{
		var openResult = await Win32FolderEnumerationSource.TryOpenAsync(path, cancellationToken);
		return MapOpenResult(openResult);
	}

	/// <summary>Maps a Win32 source-open result to a provider-neutral navigation result.</summary>
	internal static NavigationScopeOpenResult MapOpenResult(
		Win32FolderEnumerationOpenResult openResult)
		=> openResult.Status switch
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

	private static NavigationUnavailableReason? MapFallbackReason(int nativeErrorCode)
		=> (WIN32_ERROR)nativeErrorCode switch
		{
			WIN32_ERROR.ERROR_ACCESS_DENIED => NavigationUnavailableReason.AccessDenied,
			WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND => NavigationUnavailableReason.NotFound,
			_ => null,
		};
}
