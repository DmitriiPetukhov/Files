using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32IncrementalSortedAccumulatorTests
{
	[TestMethod]
	public void AddsEachBatchToOneOrderedSnapshot()
	{
		var accumulator = new Win32IncrementalSortedAccumulator<int>(Comparer<int>.Default);

		var firstSnapshot = accumulator.AddBatch(new[] { 7, 1, 5 });
		var secondSnapshot = accumulator.AddBatch(new[] { 4, 2 });

		CollectionAssert.AreEqual(new[] { 1, 5, 7 }, firstSnapshot.ToArray());
		CollectionAssert.AreEqual(new[] { 1, 2, 4, 5, 7 }, secondSnapshot.ToArray());
	}

	[TestMethod]
	public void PreviousSnapshotRemainsUnchangedAfterNextBatch()
	{
		var accumulator = new Win32IncrementalSortedAccumulator<int>(Comparer<int>.Default);

		var firstSnapshot = accumulator.AddBatch(new[] { 3, 1 });
		_ = accumulator.AddBatch(new[] { 2 });

		CollectionAssert.AreEqual(new[] { 1, 3 }, firstSnapshot.ToArray());
	}

	[TestMethod]
	public void RetainsItemsThatShareThePrimarySortValue()
	{
		var accumulator = new Win32IncrementalSortedAccumulator<SortValue>(SortValueComparer.Instance);

		var snapshot = accumulator.AddBatch(new[]
		{
			new SortValue(2, "second"),
			new SortValue(1, "first"),
			new SortValue(2, "another"),
		});

		CollectionAssert.AreEqual(new[] { "first", "another", "second" }, snapshot.Select(x => x.Name).ToArray());
	}

	private sealed record SortValue(int Value, string Name);

	private sealed class SortValueComparer : IComparer<SortValue>
	{
		public static SortValueComparer Instance { get; } = new();

		public int Compare(SortValue? x, SortValue? y)
		{
			var result = Comparer<int>.Default.Compare(x?.Value ?? 0, y?.Value ?? 0);
			return result != 0 ? result : string.CompareOrdinal(x?.Name, y?.Name);
		}
	}
}
