using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Contracts;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Services;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies the cheap Win32 publication boundary.</summary>
[TestClass]
public sealed class Win32FolderPublicationAdapterTests
{
	/// <summary>Ensures accepted items retain source order and arrive before source completion.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PublishesAcceptedBatchBeforeSourceCompletion()
	{
		var first = CreateItem("first");
		var second = CreateItem("second");
		var source = new ScriptedFolderEnumerationSource(
			[(IReadOnlyCollection<FolderItem>)[first], [second]],
			TimeSpan.FromMilliseconds(600));
		var settings = new StubFoldersSettingsService
		{
			ShowDotFiles = true,
		};
		var adapter = new Win32FolderPublicationAdapter(source, settings);

		await using var enumerator = adapter.EnumerateAsync(CancellationToken.None).GetAsyncEnumerator();
		Assert.IsTrue(await enumerator.MoveNextAsync());

		Assert.IsFalse(source.EnumerationCompleted);
		CollectionAssert.AreEqual(new[] { first }, enumerator.Current.Items.ToArray());
	}

	/// <summary>Ensures hidden, protected-system, and dot-file rules match the legacy adapter.</summary>
	[TestMethod]
	public async Task EnumerateAsync_AppliesVisibilitySettings()
	{
		var settings = new StubFoldersSettingsService
		{
			ShowHiddenItems = false,
			ShowProtectedSystemFiles = false,
			ShowDotFiles = false,
		};
		var hidden = CreateWin32Item("hidden", isHidden: true);
		var protectedSystem = CreateWin32Item("protected", isHidden: true, isSystem: true);
		var dotFile = CreateWin32Item(".dot");
		var visible = CreateWin32Item("visible");
		var source = new ScriptedFolderEnumerationSource(
			[(IReadOnlyCollection<FolderItem>)[hidden], [protectedSystem], [dotFile], [visible]]);
		var adapter = new Win32FolderPublicationAdapter(source, settings);
		var batches = new List<FolderEnumerationBatch<FolderItem>>();

		await foreach (var batch in adapter.EnumerateAsync(CancellationToken.None))
			batches.Add(batch);

		Assert.AreEqual(1, batches.Count);
		CollectionAssert.AreEqual(new[] { visible }, batches[0].Items.ToArray());

		settings.ShowHiddenItems = true;
		settings.ShowProtectedSystemFiles = true;
		settings.ShowDotFiles = true;
		await using var visibleSource = new ScriptedFolderEnumerationSource(
			[(IReadOnlyCollection<FolderItem>)[hidden, protectedSystem, dotFile, visible]]);
		var visibleAdapter = new Win32FolderPublicationAdapter(visibleSource, settings);
		var allItems = new List<FolderItem>();

		await foreach (var batch in visibleAdapter.EnumerateAsync(CancellationToken.None))
			allItems.AddRange(batch.Items);

		CollectionAssert.AreEqual(new[] { hidden, protectedSystem, dotFile, visible }, allItems);
	}

	/// <summary>Ensures a batch filtered to zero items does not emit an empty intermediate batch.</summary>
	[TestMethod]
	public async Task EnumerateAsync_DropsFullyFilteredBatch()
	{
		var settings = new StubFoldersSettingsService();
		var source = new ScriptedFolderEnumerationSource(
			[(IReadOnlyCollection<FolderItem>)[CreateWin32Item(".hidden")]]);
		var adapter = new Win32FolderPublicationAdapter(source, settings);
		var batches = new List<FolderEnumerationBatch<FolderItem>>();

		await foreach (var batch in adapter.EnumerateAsync(CancellationToken.None))
			batches.Add(batch);

		Assert.AreEqual(0, batches.Count);
	}

	private static FolderItem CreateItem(string name)
		=> new(new FolderItemKey("test", name), name, FolderItemKind.File, null, null);

	private static FolderItem CreateWin32Item(
		string name,
		bool isHidden = false,
		bool isSystem = false)
	{
		var findData = new Win32PInvoke.WIN32_FIND_DATA
		{
			cFileName = name,
			dwFileAttributes = (isHidden ? (uint)FileAttributes.Hidden : 0u) |
				(isSystem ? (uint)FileAttributes.System : 0u),
		};

		return new FolderItem(
			new FolderItemKey("win32", Path.Combine(Path.GetTempPath(), name)),
			name,
			FolderItemKind.File,
			null,
			new Win32FolderItemData(findData));
	}
}
