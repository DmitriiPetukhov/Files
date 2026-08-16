using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App.Data.Contracts;
using Files.App.Data.Items;
using Files.App.Helpers;
using Files.App.Services;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.SizeProvider;
using Files.App.UnitTests.TestDoubles.Services;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

	/// <summary>Ensures multiple provider batches publish after 32 accepted main items.</summary>
	[TestMethod]
	public async Task EnumerateAsync_CoalescesProviderBatchesAtMainItemThreshold()
	{
		var batches = Enumerable.Range(0, 32)
			.Select(index => (IReadOnlyCollection<FolderItem>)[CreateFolderItem($"item-{index}")])
			.ToArray();
		await using var source = new ScriptedFolderEnumerationSource(batches);
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create());
		var publishedBatches = new List<IReadOnlyCollection<ListedItem>>();

		await adapter.EnumerateAsync(
			batch =>
			{
				publishedBatches.Add(batch);
				return Task.CompletedTask;
			},
			CancellationToken.None);

		Assert.AreEqual(1, publishedBatches.Count);
		Assert.AreEqual(32, publishedBatches[0].Count);
	}

	/// <summary>Ensures slow provider batches publish when the 500 millisecond interval elapses.</summary>
	[TestMethod]
	public async Task EnumerateAsync_CoalescesProviderBatchesAtTimeThreshold()
	{
		var batches = new IReadOnlyCollection<FolderItem>[]
		{
			[CreateFolderItem("first")],
			[CreateFolderItem("second")],
		};
		await using var source = new ScriptedFolderEnumerationSource(
			batches,
			TimeSpan.FromMilliseconds(600));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create());
		var publishedBatches = new List<IReadOnlyCollection<ListedItem>>();

		await adapter.EnumerateAsync(
			batch =>
			{
				publishedBatches.Add(batch);
				return Task.CompletedTask;
			},
			CancellationToken.None);

		Assert.AreEqual(1, publishedBatches.Count);
		CollectionAssert.AreEqual(
			new[] { "first", "second" },
			publishedBatches[0].Select(item => item.ItemNameRaw).ToArray());
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

	/// <summary>Ensures the default legacy path applies settings without a callback seam.</summary>
	[TestMethod]
	public async Task EnumerateAsync_UsesDefaultLegacyMaterializationPath()
	{
		var settings = new StubUserSettingsService();
		settings.FoldersSettings.AreAlternateStreamsVisible = true;
		settings.FoldersSettings.CalculateFolderSizes = true;
		var iconCache = new StubIconCacheService();
		var sizeProvider = new RecordingSizeProvider();
		var iconWarmUpQueue = new IconWarmUpQueue(
			iconCache,
			NullLogger<IconWarmUpQueue>.Instance,
			capacity: 1,
			workerCount: 1);
		var filePath = Path.Combine(FolderPath, "visible.txt");
		var folderPath = Path.Combine(FolderPath, "visible-folder");
		File.WriteAllText(filePath, "visible content");
		Directory.CreateDirectory(folderPath);
		File.WriteAllText(filePath + ":metadata", "alternate stream");
		using var serviceProvider = new ServiceCollection()
			.AddSingleton<IUserSettingsService>(settings)
			.AddSingleton<IFoldersSettingsService>(settings.FoldersSettings)
			.AddSingleton<IStartMenuService, StubStartMenuService>()
			.AddSingleton<IFileTagsSettingsService, StubFileTagsSettingsService>()
			.AddSingleton<IDateTimeFormatter, StubDateTimeFormatter>()
			.AddSingleton<ISizeProvider>(sizeProvider)
			.AddSingleton<IStorageCacheService, StorageCacheService>()
			.AddSingleton(iconWarmUpQueue)
			.BuildServiceProvider();
		Ioc.Default.ConfigureServices(serviceProvider);

		var handle = new ScriptedWin32FindHandle(
			[CreateFindData("visible-folder", isDirectory: true)]);
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("visible.txt"));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create(),
			legacyRootPath: FolderPath,
			isGitRepo: true);

		var finalItems = await adapter.EnumerateAsync(
			_ => Task.CompletedTask,
			CancellationToken.None);

		await iconWarmUpQueue.DisposeAsync();

		var file = finalItems.Single(item => item.ItemPath == filePath);
		var folder = finalItems.Single(item => item.ItemPath == folderPath);
		Assert.IsInstanceOfType(file, typeof(GitItem));
		Assert.IsInstanceOfType(folder, typeof(GitItem));
		Assert.IsTrue(finalItems.Any(item => item is AlternateStreamItem));
		CollectionAssert.Contains(iconCache.RequestedPaths, filePath);
		CollectionAssert.Contains(iconCache.RequestedPaths, folderPath);
		CollectionAssert.Contains(sizeProvider.TryGetSizePaths, folderPath);
		CollectionAssert.Contains(sizeProvider.UpdatePaths, folderPath);
	}

	/// <summary>Ensures an item-scoped overflow buffer does not release the persistent pool buffer early.</summary>
	[TestMethod]
	public async Task EnumerateAsync_KeepsPersistentBufferRentedWhilePublishingOverflowItem()
	{
		var settings = new StubUserSettingsService();
		settings.FoldersSettings.AreAlternateStreamsVisible = true;
		var iconCache = new StubIconCacheService();
		var iconWarmUpQueue = new IconWarmUpQueue(
			iconCache,
			NullLogger<IconWarmUpQueue>.Instance,
			capacity: 1,
			workerCount: 1);
		var filePath = Path.Combine(FolderPath, "overflow.txt");
		File.WriteAllText(filePath, "overflow content");
		for (var index = 0; index < 257; index++)
			File.WriteAllText($"{filePath}:stream-{index}", "alternate stream");

		using var serviceProvider = new ServiceCollection()
			.AddSingleton<IUserSettingsService>(settings)
			.AddSingleton<IFoldersSettingsService>(settings.FoldersSettings)
			.AddSingleton<IStartMenuService, StubStartMenuService>()
			.AddSingleton<IFileTagsSettingsService, StubFileTagsSettingsService>()
			.AddSingleton<IDateTimeFormatter, StubDateTimeFormatter>()
			.AddSingleton<ISizeProvider>(new RecordingSizeProvider())
			.AddSingleton<IStorageCacheService, StorageCacheService>()
			.AddSingleton(iconWarmUpQueue)
			.BuildServiceProvider();
		Ioc.Default.ConfigureServices(serviceProvider);

		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("overflow.txt"));
		var adapter = new Win32ListedItemEnumerationAdapter(
			source,
			FolderItemListedItemProjectionTestFactory.Create(),
			legacyRootPath: FolderPath);
		var availableBefore = ListedItemArrayPool.Shared.AvailableCount;
		var publishedBatches = new List<IReadOnlyCollection<ListedItem>>();

		var finalItems = await adapter.EnumerateAsync(
			batch =>
			{
				Assert.AreEqual(Math.Max(0, availableBefore - 1), ListedItemArrayPool.Shared.AvailableCount);
				publishedBatches.Add(batch);
				return Task.CompletedTask;
			},
			CancellationToken.None);

		await iconWarmUpQueue.DisposeAsync();

		Assert.AreEqual(258, finalItems.Count);
		Assert.AreEqual(1, publishedBatches.Count);
		Assert.AreEqual(258, publishedBatches[0].Count);
	}

	private static Win32PInvoke.WIN32_FIND_DATA CreateFindData(
		string name,
		bool isDirectory = false,
		bool isHidden = false)
	{
		var fileTime = DateTime.UtcNow.ToFileTimeUtc();
		var nativeFileTime = new System.Runtime.InteropServices.ComTypes.FILETIME
		{
			dwHighDateTime = (int)(fileTime >> 32),
			dwLowDateTime = (int)fileTime,
		};

		return new()
		{
			cFileName = name,
			dwFileAttributes = (isDirectory ? (uint)FileAttributes.Directory : 0u) |
			(isHidden ? (uint)FileAttributes.Hidden : 0u),
			ftCreationTime = nativeFileTime,
			ftLastAccessTime = nativeFileTime,
			ftLastWriteTime = nativeFileTime,
		};
	}

	private FolderItem CreateFolderItem(string name)
		=> new(
			new FolderItemKey("win32", Path.Combine(FolderPath, name)),
			name,
			FolderItemKind.File,
			null,
			null);

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
