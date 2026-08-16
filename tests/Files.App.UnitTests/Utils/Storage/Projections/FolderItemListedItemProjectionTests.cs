using System.IO;
using Files.App.Utils.Storage.Contracts;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;
using Files.App.UnitTests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Projections;

/// <summary>Verifies projection of provider-neutral items into UI items.</summary>
[TestClass]
public sealed class FolderItemListedItemProjectionTests
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

	/// <summary>Ensures file identity and metadata are preserved by projection.</summary>
	[TestMethod]
	public void Project_FileSnapshot_PreservesIdentityAndMetadata()
	{
		var item = new FolderItem(
			new FolderItemKey("win32", Path.Combine(FolderPath, "report.txt")),
			"report.txt",
			FolderItemKind.File,
			new FolderItemMetadata(2048, null, null),
			null);

		var projected = FolderItemListedItemProjectionTestFactory.Create().Project(item);

		Assert.AreEqual("report.txt", projected.ItemNameRaw);
		Assert.AreEqual(item.Key.OpaqueId, projected.ItemPath);
		Assert.AreEqual(Windows.Storage.StorageItemTypes.File, projected.PrimaryItemAttribute);
		Assert.AreEqual(".txt", projected.FileExtension);
		Assert.AreEqual(2048, projected.FileSizeBytes);
		Assert.IsFalse(string.IsNullOrWhiteSpace(projected.ItemType));
	}

	/// <summary>Ensures folder snapshots are projected as folders without file size data.</summary>
	[TestMethod]
	public void Project_FolderSnapshot_UsesFolderPresentation()
	{
		var item = new FolderItem(
			new FolderItemKey("win32", Path.Combine(FolderPath, "Documents")),
			"Documents",
			FolderItemKind.Folder,
			new FolderItemMetadata(null, null, null),
			null);

		var projected = FolderItemListedItemProjectionTestFactory.Create().Project(item);

		Assert.AreEqual(Windows.Storage.StorageItemTypes.Folder, projected.PrimaryItemAttribute);
		Assert.IsTrue(string.IsNullOrEmpty(projected.FileExtension));
		Assert.IsTrue(string.IsNullOrEmpty(projected.FileSize));
		Assert.AreEqual(0, projected.FileSizeBytes);
		Assert.AreEqual(item.Name, projected.ItemNameRaw);
	}

}
