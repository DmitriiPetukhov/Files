using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Projections;

/// <summary>Verifies navigation-scoped compatibility overlay projection.</summary>
[TestClass]
public sealed class FolderItemListedItemPublicationProjectionTests
{
	/// <summary>Ensures cheap accumulated state is projected in source order.</summary>
	[TestMethod]
	public void ProjectState_ProjectsCheapItemsInSourceOrder()
	{
		var first = CreateItem("first.txt");
		var second = CreateItem("second");
		var projection = FolderItemListedItemProjectionTestFactory.Create();

		var result = projection.ProjectState(new FolderPublicationState(
			1,
			ImmutableArray.Create(first, second)));

		CollectionAssert.AreEqual(
			new[] { "first.txt", "second" },
			result.Select(item => item.ItemNameRaw).ToArray());
	}

	/// <summary>Ensures a legacy subtype replaces the cheap placeholder and remains stable.</summary>
	[TestMethod]
	public void ApplyLegacyOverlay_ReusesStableLegacyPrimaryInstance()
	{
		var item = CreateItem("tracked.txt");
		var projection = FolderItemListedItemProjectionTestFactory.Create();
		var cheapItem = projection.Project(item);
		var legacyItem = new GitItem
		{
			ItemPath = item.Key.OpaqueId,
			ItemNameRaw = item.Name,
		};

		projection.ApplyLegacyOverlay(item.Key, legacyItem, []);

		var firstResult = projection.ProjectState(new FolderPublicationState(
			2,
			ImmutableArray.Create(item)));
		var secondResult = projection.ProjectState(new FolderPublicationState(
			3,
			ImmutableArray.Create(item)));

		Assert.AreNotSame(cheapItem, firstResult.Single());
		Assert.AreSame(legacyItem, firstResult.Single());
		Assert.AreSame(legacyItem, secondResult.Single());
		Assert.IsInstanceOfType(secondResult.Single(), typeof(GitItem));
	}

	/// <summary>Ensures alternate-stream compatibility entries remain after their primary item.</summary>
	[TestMethod]
	public void ApplyLegacyOverlay_PreservesAdditionalEntriesAfterPrimary()
	{
		var item = CreateItem("data.txt");
		var projection = FolderItemListedItemProjectionTestFactory.Create();
		var legacyItem = new ListedItem(null!)
		{
			ItemPath = item.Key.OpaqueId,
			ItemNameRaw = item.Name,
		};
		var alternateStream = new AlternateStreamItem
		{
			ItemPath = $"{item.Key.OpaqueId}:metadata",
			ItemNameRaw = "metadata",
		};

		projection.ApplyLegacyOverlay(item.Key, legacyItem, new[] { alternateStream });

		var result = projection.ProjectState(new FolderPublicationState(
			1,
			ImmutableArray.Create(item)));

		Assert.AreEqual(2, result.Count);
		Assert.AreSame(legacyItem, result[0]);
		Assert.AreSame(alternateStream, result[1]);
	}

	/// <summary>Ensures stale enrichment cleanup cannot remove a newer overlay revision.</summary>
	[TestMethod]
	public void RemoveLegacyOverlay_RejectsStaleRevision()
	{
		var item = CreateItem("tracked.txt");
		var projection = FolderItemListedItemProjectionTestFactory.Create();
		var legacyItem = new ListedItem(null!)
		{
			ItemPath = item.Key.OpaqueId,
			ItemNameRaw = item.Name,
		};

		projection.ApplyLegacyOverlay(item.Key, legacyItem, [], expectedRevision: 2);
		projection.RemoveLegacyOverlay(item.Key, legacyItem, expectedRevision: 1);

		var result = projection.ProjectState(new FolderPublicationState(
			1,
			ImmutableArray.Create(item)));

		Assert.AreSame(legacyItem, result.Single());
	}

	private static FolderItem CreateItem(string name)
		=> new(new FolderItemKey("win32", Path.Combine(Path.GetTempPath(), name)), name, FolderItemKind.File, null, null);
}
