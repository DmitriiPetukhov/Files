using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Files.App.Data.Enums;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class SortingHelperTests
{
	[TestMethod]
	public void SortsNamesNaturally()
	{
		var items = new[]
		{
			CreateFolder("file10"),
			CreateFolder("file2"),
			CreateFolder("file1")
		};
		var comparer = SortingHelper.GetComparer(SortOption.Name, SortDirection.Ascending, true, false);

		var sorted = items.OrderBy(item => item, comparer).Select(item => item.Name).ToArray();

		CollectionAssert.AreEqual(new[] { "file1", "file2", "file10" }, sorted);
	}

	[TestMethod]
	public void IncrementalPublicationPreservesNaturalNameOrdering()
	{
		var items = new[]
		{
			CreateFolder("file10"),
			CreateFolder("file2"),
			CreateFolder("file1")
		};
		var session = new Win32FolderPublicationSession<ListedItem>(
			SortingHelper.GetComparer(SortOption.Name, SortDirection.Ascending, true, false));

		Assert.IsTrue(session.TryAppend(new[] { items[0] }, CancellationToken.None, out _));
		Assert.IsTrue(session.TryAppend(items[1..], CancellationToken.None, out var snapshot));

		CollectionAssert.AreEqual(new[] { "file1", "file2", "file10" }, snapshot!.Select(item => item.Name).ToArray());
	}

	[TestMethod]
	public void CachesPrimarySortKeysAcrossComparisons()
	{
		var items = Enumerable.Range(0, 16)
			.Reverse()
			.Select(index => CountingListedItem.Create($"item{index}"))
			.ToArray();
		var comparer = SortingHelper.GetComparer(SortOption.Name, SortDirection.Ascending, true, false);

		_ = items.OrderBy(item => item, comparer).ToArray();

		Assert.IsTrue(items.All(item => item.NameAccessCount == 1));
	}

	[TestMethod]
	public void RebuildsFinalOrderWhenFileTagsChangeAfterEarlyPublication()
	{
		var first = CreateFolder("a");
		var second = CreateFolder("b");
		var session = new Win32FolderPublicationSession<ListedItem>(
			SortingHelper.GetComparer(SortOption.FileTag, SortDirection.Ascending, true, false));

		Assert.IsTrue(session.TryAppend(new[] { first, second }, CancellationToken.None, out _));
		second.FileTags = ["tag"];

		Assert.IsTrue(session.TryReplaceFinal(
			new[] { first, second },
			SortingHelper.GetComparer(SortOption.FileTag, SortDirection.Ascending, true, false),
			CancellationToken.None,
			out var finalSnapshot));

		CollectionAssert.AreEqual(new[] { "b", "a" }, finalSnapshot!.Select(item => item.Name).ToArray());
	}

	[TestMethod]
	public void CachesFileTagSortKeysAcrossComparisons()
	{
		var items = Enumerable.Range(0, 16)
			.Reverse()
			.Select(index => CreateFolder($"item{index}"))
			.ToArray();
		var extractionCount = 0;
		var comparer = SortingHelper.GetComparer(
			SortOption.FileTag,
			SortDirection.Ascending,
			true,
			false,
			item =>
			{
				extractionCount++;
				return item.FileTags?.FirstOrDefault() ?? string.Empty;
			});

		_ = items.OrderBy(item => item, comparer).ToArray();

		Assert.AreEqual(items.Length, extractionCount);
	}

	private static ListedItem CreateFolder(string name)
	{
		var item = (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));
		item.ItemNameRaw = name;
		item.ItemPath = $"C:\\{name}";
		item.PrimaryItemAttribute = Windows.Storage.StorageItemTypes.Folder;
		return item;
	}

	private sealed class CountingListedItem : ListedItem
	{
		private string name = string.Empty;

		public int NameAccessCount { get; private set; }

		public static CountingListedItem Create(string name)
		{
			var item = (CountingListedItem)RuntimeHelpers.GetUninitializedObject(typeof(CountingListedItem));
			item.name = name;
			return item;
		}

		public override string Name
		{
			get
			{
				NameAccessCount++;
				return name;
			}
		}
	}
}
