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

	private static ListedItem CreateFolder(string name)
	{
		var item = (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));
		item.ItemNameRaw = name;
		item.ItemPath = $"C:\\{name}";
		item.PrimaryItemAttribute = Windows.Storage.StorageItemTypes.Folder;
		return item;
	}
}
