using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Navigation;

/// <summary>Verifies incremental navigation graph composition.</summary>
[TestClass]
public sealed class NavigationScopeFactoryTests
{
	private TemporaryTestDirectory temporaryDirectory = null!;

	private string FolderPath => temporaryDirectory.DirectoryPath;

	/// <summary>Creates an isolated directory for the test.</summary>
	[TestInitialize]
	public void CreateTemporaryDirectory()
		=> temporaryDirectory = new TemporaryTestDirectory();

	/// <summary>Removes the isolated directory after the test.</summary>
	[TestCleanup]
	public void CleanupTemporaryDirectory()
		=> temporaryDirectory.Dispose();

	/// <summary>Ensures the initial graph creates a scope with the bound Win32 enumeration source.</summary>
	[TestMethod]
	public async Task Create_ContainsWin32SourceForWin32Folder()
	{
		File.WriteAllText(Path.Combine(FolderPath, "item.txt"), "item");
		var handle = OpenSearchHandle(FolderPath, out var firstFindData);
		var factory = new NavigationScopeFactory();

		await using var scope = factory.Create(
			new FolderReference("win32", FolderPath),
			handle,
			firstFindData);

		var batches = new List<FolderEnumerationBatch<FolderItem>>();
		await foreach (var batch in scope.EnumerationSource.EnumerateAsync())
			batches.Add(batch);

		Assert.AreEqual(typeof(Win32FolderEnumerationSource), scope.EnumerationSource.GetType());
		Assert.IsTrue(batches.Count > 0);
	}

	/// <summary>Ensures unsupported providers are rejected before scope creation.</summary>
	[TestMethod]
	public void Create_RejectsUnsupportedProvider()
	{
		var factory = new NavigationScopeFactory();

		var exception = CaptureException<ArgumentException>(() =>
			factory.Create(
				new FolderReference("ftp", FolderPath),
				IntPtr.Zero,
				new Win32PInvoke.WIN32_FIND_DATA()));

		Assert.AreEqual("folder", exception.ParamName);
	}

	/// <summary>Ensures a missing folder reference is rejected before scope creation.</summary>
	[TestMethod]
	public void Create_RejectsMissingFolder()
	{
		var factory = new NavigationScopeFactory();

		var exception = CaptureException<ArgumentNullException>(() =>
			factory.Create(
				null!,
				IntPtr.Zero,
				new Win32PInvoke.WIN32_FIND_DATA()));

		Assert.AreEqual("folder", exception.ParamName);
	}

	private static IntPtr OpenSearchHandle(
		string folderPath,
		out Win32PInvoke.WIN32_FIND_DATA firstFindData)
	{
		var handle = Win32PInvoke.FindFirstFileExFromApp(
			Path.Combine(folderPath, "*.*"),
			Win32PInvoke.FINDEX_INFO_LEVELS.FindExInfoBasic,
			out firstFindData,
			Win32PInvoke.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
			IntPtr.Zero,
			Win32PInvoke.FIND_FIRST_EX_LARGE_FETCH);

		Assert.AreNotEqual(IntPtr.Zero, handle);
		Assert.AreNotEqual(Win32PInvoke.INVALID_HANDLE_VALUE, handle);
		return handle;
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
