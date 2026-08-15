// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Integration;

/// <summary>Verifies the Win32 source against the native filesystem boundary.</summary>
[TestClass]
public sealed class Win32FolderEnumerationSourceIntegrationTests
{
	/// <summary>Ensures an existing folder can enumerate and resolve a current file.</summary>
	[TestMethod]
	public async Task TryCreate_EnumeratesAndResolvesExistingFile()
	{
		var folderPath = CreateTempDirectory();
		var filePath = Path.Combine(folderPath, "current.txt");
		File.WriteAllText(filePath, "current");

		try
		{
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
		finally
		{
			DeleteTempDirectory(folderPath);
		}
	}

	/// <summary>Ensures an empty native folder produces no batches.</summary>
	[TestMethod]
	public async Task TryCreate_EnumeratesEmptyFolderWithoutBatches()
	{
		var folderPath = CreateTempDirectory();

		try
		{
			var source = Win32FolderEnumerationSource.TryCreate(folderPath);
			Assert.IsNotNull(source);
			await using (source!)
			{
				var batches = new List<FolderEnumerationBatch<FolderItem>>();
				await foreach (var batch in source.EnumerateAsync())
					batches.Add(batch);

				Assert.AreEqual(0, batches.Count);
			}
		}
		finally
		{
			DeleteTempDirectory(folderPath);
		}
	}

	/// <summary>Ensures missing native folders raise their Win32 error.</summary>
	[TestMethod]
	public void TryCreate_ThrowsPathNotFoundForMissingFolder()
	{
		var parentPath = CreateTempDirectory();
		var missingPath = Path.Combine(parentPath, "missing");

		try
		{
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
		finally
		{
			DeleteTempDirectory(parentPath);
		}
	}

	/// <summary>Ensures missing native items resolve to null.</summary>
	[TestMethod]
	public async Task ResolveAsync_ReturnsNullForMissingNativeItem()
	{
		var folderPath = CreateTempDirectory();

		try
		{
			var source = Win32FolderEnumerationSource.TryCreate(folderPath);
			Assert.IsNotNull(source);
			await using (source!)
			{
				var item = await source.ResolveAsync(
					new FolderItemKey("win32", Path.Combine(folderPath, "missing.txt")));

				Assert.IsNull(item);
			}
		}
		finally
		{
			DeleteTempDirectory(folderPath);
		}
	}

	private static string CreateTempDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"FilesUnitTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteTempDirectory(string path)
	{
		if (Directory.Exists(path))
			Directory.Delete(path, recursive: true);
	}
}
