using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies the compatibility projection over the provider-neutral Win32 source.</summary>
[TestClass]
public sealed class Win32ListedItemEnumerationAdapterTests
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

	/// <summary>Ensures source batches are projected and published in their original order.</summary>
	[TestMethod]
	public async Task EnumerateAsync_ProjectsAndPublishesOrderedItems()
	{
		var handle = new ScriptedWin32FindHandle(
		[
			CreateFindData("second.txt"),
			CreateFindData("third", isDirectory: true)
		]);
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("first.txt"));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create());
		var publishedBatches = new List<IReadOnlyCollection<ListedItem>>();

		var finalItems = await adapter.EnumerateAsync(
			batch =>
			{
				publishedBatches.Add(batch);
				return Task.CompletedTask;
			},
			CancellationToken.None);

		CollectionAssert.AreEqual(
			new[] { "first.txt", "second.txt", "third" },
			finalItems.Select(item => item.ItemNameRaw).ToArray());
		Assert.AreEqual(1, publishedBatches.Count);
		CollectionAssert.AreEqual(
			new[] { "first.txt", "second.txt", "third" },
			publishedBatches[0].Select(item => item.ItemNameRaw).ToArray());
		Assert.AreEqual(1, handle.DisposeCount);
		await source.DisposeAsync();
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures the compatibility materializer receives provider-specific Win32 snapshots.</summary>
	[TestMethod]
	public async Task EnumerateAsync_UsesLegacyMaterializerForWin32Snapshots()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item.txt"));
		FolderItem? materializedItem = null;
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create(),
			legacyRootPath: FolderPath,
			legacyMaterializer: async (items, _) =>
			{
				materializedItem = items.Single();
				return [CreateListedItem(materializedItem.Name)];
			});

		var finalItems = await adapter.EnumerateAsync(_ => Task.CompletedTask, CancellationToken.None);

		Assert.IsNotNull(materializedItem);
		Assert.IsTrue(materializedItem.ProviderData is Win32FolderItemData);
		Assert.AreEqual("item.txt", finalItems.Single().ItemNameRaw);
	}

	/// <summary>Ensures source failures propagate through the compatibility adapter.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PropagatesSourceFailure()
	{
		var failure = new InvalidOperationException("find failed");
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>(), failure);
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create());

		var exception = await CaptureExceptionAsync<InvalidOperationException>(() =>
			adapter.EnumerateAsync(_ => Task.CompletedTask, CancellationToken.None));

		Assert.AreSame(failure, exception);
		Assert.AreEqual(1, handle.DisposeCount);
		await source.DisposeAsync();
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures cancellation propagates while source lifetime remains scope-owned.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PropagatesCancellation()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create());
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await CaptureExceptionAsync<OperationCanceledException>(() =>
			adapter.EnumerateAsync(_ => Task.CompletedTask, cancellationTokenSource.Token));

		Assert.AreEqual(1, handle.DisposeCount);
		await source.DisposeAsync();
		Assert.AreEqual(1, handle.DisposeCount);
	}

	private static Win32PInvoke.WIN32_FIND_DATA CreateFindData(string name, bool isDirectory = false)
		=> new()
		{
			cFileName = name,
			dwFileAttributes = isDirectory ? (uint)FileAttributes.Directory : 0u,
		};

	private static ListedItem CreateListedItem(string name)
	{
		var item = (ListedItem)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));
		item.ItemNameRaw = name;
		return item;
	}

	private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action)
		where TException : Exception
	{
		try
		{
			await action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null;
	}
}
