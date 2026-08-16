// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Win32.Foundation;

namespace Files.App.UnitTests.Utils.Storage.Navigation;

/// <summary>Verifies Win32 source outcomes are mapped to navigation results.</summary>
[TestClass]
public sealed class Win32NavigationScopeProviderTests
{
	/// <summary>Ensures access-denied errors select the fallback navigation result.</summary>
	[TestMethod]
	public void MapOpenResult_MapsAccessDeniedToFallback()
	{
		var result = Win32NavigationScopeProvider.MapOpenResult(new Win32FolderEnumerationOpenResult(
			Win32FolderEnumerationOpenStatus.InvalidHandle,
			null,
			null,
			(int)WIN32_ERROR.ERROR_ACCESS_DENIED));

		Assert.AreEqual(NavigationScopeOpenStatus.Fallback, result.Status);
		Assert.AreEqual(NavigationUnavailableReason.AccessDenied, result.FailureReason);
	}

	/// <summary>Ensures a zero handle selects the unavailable navigation result.</summary>
	[TestMethod]
	public void MapOpenResult_MapsZeroHandleToUnavailable()
	{
		var result = Win32NavigationScopeProvider.MapOpenResult(new Win32FolderEnumerationOpenResult(
			Win32FolderEnumerationOpenStatus.ZeroHandle,
			null,
			null,
			0));

		Assert.AreEqual(NavigationScopeOpenStatus.Unavailable, result.Status);
		Assert.IsNull(result.Scope);
		Assert.AreEqual(NavigationUnavailableReason.DriveUnplugged, result.FailureReason);
	}
}
