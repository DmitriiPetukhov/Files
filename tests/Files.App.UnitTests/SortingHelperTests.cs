using System.Linq;
using System.Runtime.CompilerServices;
using Files.App.Data.Enums;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Storage;

namespace Files.App.UnitTests;

[TestClass]
public sealed class SortingHelperTests
{
	[TestMethod]
	public void GetComparer_UsesNaturalNameOrdering()
	{
		var items = new[]
		{
			CreateItem("file10"),
			CreateItem("file2")
		};

		var ordered = items.OrderBy(item => item, SortingHelper.GetComparer(SortOption.Name, SortDirection.Ascending, true, false)).ToArray();

		CollectionAssert.AreEqual(["file2", "file10"], ordered.Select(item => item.Name).ToArray());
	}

	[TestMethod]
	public void GetComparer_PreservesFolderPriority()
	{
		var folder = CreateItem("z-folder", StorageItemTypes.Folder);
		var file = CreateItem("a-file", StorageItemTypes.File);
		var comparer = SortingHelper.GetComparer(SortOption.Name, SortDirection.Ascending, false, false);

		var ordered = new[] { file, folder }.OrderBy(item => item, comparer).ToArray();

		CollectionAssert.AreEqual([folder, file], ordered);
	}

	[TestMethod]
	public void GetComparer_UsesNameAsTieBreakerForNonNameSorts()
	{
		var later = CreateItem("z-item");
		later.FileSizeBytes = 10;
		var earlier = CreateItem("a-item");
		earlier.FileSizeBytes = 10;

		var comparer = SortingHelper.GetComparer(SortOption.Size, SortDirection.Ascending, true, false);
		var ordered = new[] { later, earlier }.OrderBy(item => item, comparer).ToArray();

		CollectionAssert.AreEqual([earlier, later], ordered);
	}

	private static ListedItem CreateItem(string name, StorageItemTypes itemType = StorageItemTypes.Folder)
	{
		var item = (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));
		item.PrimaryItemAttribute = itemType;
		item.ItemNameRaw = name;
		return item;
	}
}
