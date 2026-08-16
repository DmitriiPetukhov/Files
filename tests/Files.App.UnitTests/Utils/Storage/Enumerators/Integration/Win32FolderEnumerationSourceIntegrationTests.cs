// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using Files.App.Helpers;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Integration;

/// <summary>Verifies the Win32 source against the native filesystem boundary.</summary>
[TestClass]
public sealed class Win32FolderEnumerationSourceIntegrationTests
{
	private TemporaryTestDirectory temporaryDirectory = null!;

	/// <summary>Creates an isolated directory for the test.</summary>
	[TestInitialize]
	public void CreateTemporaryDirectory()
		=> temporaryDirectory = new TemporaryTestDirectory();

	/// <summary>Removes the isolated directory after the test.</summary>
	[TestCleanup]
	public void CleanupTemporaryDirectory()
		=> temporaryDirectory.Dispose();

	/// <summary>Ensures an existing folder can enumerate and resolve a current file.</summary>
	[TestMethod]
	public async Task TryCreate_EnumeratesAndResolvesExistingFile()
	{
		var folderPath = temporaryDirectory.DirectoryPath;
		var filePath = Path.Combine(folderPath, "current.txt");
		File.WriteAllText(filePath, "current");

		var source = Win32FolderEnumerationSource.TryCreate(folderPath);
		Assert.IsNotNull(source);
		await using (source!)
		{
			var items = new List<FolderItem>();
			await foreach (var batch in source.EnumerateAsync())
				items.AddRange(batch.Items);

			Assert.AreEqual("current.txt", items.Single().Name);
			Assert.AreEqual(FolderItemKind.File, items[0].Kind);

			var resolved = await source.ResolveAsync(new FolderItemKey("win32", filePath));

			Assert.IsNotNull(resolved);
			Assert.AreEqual(filePath, resolved!.Key.OpaqueId);
			Assert.AreEqual(FolderItemKind.File, resolved.Kind);
		}
	}

	/// <summary>Ensures an empty native folder produces no batches.</summary>
	[TestMethod]
	public async Task TryCreate_EnumeratesEmptyFolderWithoutBatches()
	{
		var source = Win32FolderEnumerationSource.TryCreate(temporaryDirectory.DirectoryPath);
		Assert.IsNotNull(source);
		await using (source!)
		{
			var batches = new List<FolderEnumerationBatch<FolderItem>>();
			await foreach (var batch in source.EnumerateAsync())
				batches.Add(batch);

			Assert.AreEqual(0, batches.Count);
		}
	}

	/// <summary>Ensures missing native folders raise their Win32 error.</summary>
	[TestMethod]
	public void TryCreate_ThrowsPathNotFoundForMissingFolder()
	{
		var missingPath = Path.Combine(temporaryDirectory.DirectoryPath, "missing");

		Win32Exception? exception = null;
		try
		{
			Win32FolderEnumerationSource.TryCreate(missingPath);
		}
		catch (Win32Exception caughtException)
		{
			exception = caughtException;
		}

		Assert.IsNotNull(exception);
		Assert.AreEqual((int)WIN32_ERROR.ERROR_PATH_NOT_FOUND, exception.NativeErrorCode);
	}

	/// <summary>Ensures a raw native handle is released when source path validation fails.</summary>
	[TestMethod]
	public void Constructor_DisposesRawHandleWhenPathValidationFails()
	{
		var filePath = Path.Combine(temporaryDirectory.DirectoryPath, "current.txt");
		File.WriteAllText(filePath, "current");
		var rawHandle = Win32PInvoke.FindFirstFileExFromApp(
			Path.Combine(temporaryDirectory.DirectoryPath, "*.*"),
			Win32PInvoke.FINDEX_INFO_LEVELS.FindExInfoBasic,
			out var findData,
			Win32PInvoke.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
			IntPtr.Zero,
			Win32PInvoke.FIND_FIRST_EX_LARGE_FETCH);
		var requiresCleanup = true;

		try
		{
			Assert.AreNotEqual(IntPtr.Zero, rawHandle);

			ArgumentException? exception = null;
			try
			{
				new Win32FolderEnumerationSource(null!, rawHandle, findData);
			}
			catch (ArgumentException caughtException)
			{
				exception = caughtException;
			}

			Assert.IsNotNull(exception);
			Assert.AreEqual("path", exception!.ParamName);
			requiresCleanup = Win32PInvoke.FindClose(rawHandle);
			Assert.IsFalse(requiresCleanup);
		}
		finally
		{
			if (requiresCleanup)
				Win32PInvoke.FindClose(rawHandle);
		}
	}

	/// <summary>Ensures missing native items resolve to null.</summary>
	[TestMethod]
	public async Task ResolveAsync_ReturnsNullForMissingNativeItem()
	{
		var source = Win32FolderEnumerationSource.TryCreate(temporaryDirectory.DirectoryPath);
		Assert.IsNotNull(source);
		await using (source!)
		{
			var item = await source.ResolveAsync(
				new FolderItemKey("win32", Path.Combine(temporaryDirectory.DirectoryPath, "missing.txt")));

			Assert.IsNull(item);
		}
	}
}
