using System;
using System.Collections.Generic;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators;

/// <summary>Verifies the immutable batch contract.</summary>
[TestClass]
public sealed class FolderEnumerationBatchTests
{
	/// <summary>Ensures construction copies items and preserves sequence.</summary>
	[TestMethod]
	public void Constructor_CopiesItemsAndRetainsSequenceNumber()
	{
		var items = new List<FolderItem> { CreateItem("one") };
		var batch = new FolderEnumerationBatch<FolderItem>(items, 7);
		items.Clear();

		Assert.AreEqual(7, batch.SequenceNumber);
		Assert.AreEqual(1, batch.Items.Count);
		Assert.AreEqual("one", batch.Items[0].Name);
	}

	/// <summary>Ensures empty batches are rejected.</summary>
	[TestMethod]
	public void Constructor_RejectsEmptyItems()
	{
		try
		{
			_ = new FolderEnumerationBatch<FolderItem>(Array.Empty<FolderItem>(), 0);
			Assert.Fail("An empty batch should be rejected.");
		}
		catch (ArgumentException)
		{
		}
	}

	private static FolderItem CreateItem(string name)
		=> new(
			new FolderItemKey("test", name),
			name,
			FolderItemKind.File,
			null,
			null);
}
