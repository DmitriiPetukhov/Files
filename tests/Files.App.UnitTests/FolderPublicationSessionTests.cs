using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationSessionTests
{
	[TestMethod]
	public void TryAppend_ReturnsOrderedAccumulatedSnapshot()
	{
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);

		Assert.IsTrue(session.TryAppend(["b", "a"], CancellationToken.None, out var firstSnapshot));
		Assert.IsTrue(session.TryAppend(["d", "c"], CancellationToken.None, out var secondSnapshot));

		CollectionAssert.AreEqual(new[] { "a", "b" }, firstSnapshot!.ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, secondSnapshot!.ToArray());
	}

	[TestMethod]
	public void TryReplaceFinal_ReplacesPreviouslyPublishedState()
	{
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);
		session.TryAppend(["a", "b"], CancellationToken.None, out _);

		Assert.IsTrue(session.TryReplaceFinal(["d", "c"], CancellationToken.None, out var snapshot));

		CollectionAssert.AreEqual(new[] { "c", "d" }, snapshot!.ToArray());
	}

	[TestMethod]
	public void TryAppend_RejectsCanceledSession()
	{
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);
		session.Cancel();

		Assert.IsFalse(session.TryAppend(["a"], CancellationToken.None, out var snapshot));
		Assert.IsNull(snapshot);
	}

	[TestMethod]
	public void TryAppend_PreservesSnapshotAfterLaterBatch()
	{
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);
		session.TryAppend(["b", "a"], CancellationToken.None, out var firstSnapshot);

		session.TryAppend(["c"], CancellationToken.None, out _);

		CollectionAssert.AreEqual(new[] { "a", "b" }, firstSnapshot!.ToArray());
	}

	[TestMethod]
	public void TryAppend_PreservesItemsWithEqualSortValues()
	{
		var session = new FolderPublicationSession<TestItem>(new TestItemComparer());
		var first = new TestItem("same", 1);
		var second = new TestItem("same", 2);

		Assert.IsTrue(session.TryAppend([first, second], CancellationToken.None, out var snapshot));

		CollectionAssert.AreEqual(new[] { first, second }, snapshot!.ToArray());
	}

	[TestMethod]
	public void TryRebuildIndex_ReordersCanonicalItemsWithoutReplacingSession()
	{
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);
		session.TryAppend(["a", "b", "c"], CancellationToken.None, out _);

		Assert.IsTrue(session.TryRebuildIndex(Comparer<string>.Create((left, right) => string.CompareOrdinal(right, left)), CancellationToken.None, out var snapshot));
		Assert.IsTrue(session.TryAppend(["d"], CancellationToken.None, out var afterAppend));

		CollectionAssert.AreEqual(new[] { "c", "b", "a" }, snapshot!.ToArray());
		CollectionAssert.AreEqual(new[] { "d", "c", "b", "a" }, afterAppend!.ToArray());
	}

	private sealed record TestItem(string Key, int Id);

	private sealed class TestItemComparer : Comparer<TestItem>
	{
		public override int Compare(TestItem? x, TestItem? y)
			=> string.CompareOrdinal(x?.Key, y?.Key);
	}
}
