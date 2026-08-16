// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Navigation;

/// <summary>Verifies navigation scope ownership and lifetime behavior.</summary>
[TestClass]
public sealed class NavigationScopeTests
{
	/// <summary>Ensures the scope exposes and disposes its enumeration source once.</summary>
	[TestMethod]
	public async Task DisposeAsync_DisposesEnumerationSourceOnce()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var source = new Win32FolderEnumerationSource(
			Path.GetTempPath(),
			handle,
			new Win32PInvoke.WIN32_FIND_DATA { cFileName = "item.txt" });
		await using var scope = new NavigationScope(source);

		Assert.AreSame(source, scope.EnumerationSource);

		await scope.DisposeAsync();
		await scope.DisposeAsync();

		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures a scope cannot be created without an enumeration source.</summary>
	[TestMethod]
	public void Constructor_RejectsMissingEnumerationSource()
	{
		var exception = CaptureException<ArgumentNullException>(() => new NavigationScope(null!));

		Assert.AreEqual("enumerationSource", exception.ParamName);
	}

	private static TException CaptureException<TException>(Action action)
		where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null;
	}
}
