using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationCoordinatorTests
{
	[TestMethod]
	public async Task EnumerateAsync_PublishesAccumulatedBatchesAndAuthoritativeFinalResult()
	{
		var snapshots = new List<IReadOnlyCollection<string>>();
		var coordinator = new FolderPublicationCoordinator<string>(
			StringComparer.Ordinal,
			snapshot =>
			{
				snapshots.Add(snapshot.ToArray());
				return Task.CompletedTask;
			});

		var finalItems = await coordinator.EnumerateAsync(
			new FakeFolderEnumerationSource<string>(
			[
				(IReadOnlyCollection<string>)["b", "a"],
				(IReadOnlyCollection<string>)["d", "c"]
			],
			["d", "c", "b", "a"]),
			CancellationToken.None);

		Assert.AreEqual(3, snapshots.Count);
		CollectionAssert.AreEqual(new[] { "a", "b" }, snapshots[0].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, snapshots[1].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, snapshots[2].ToArray());
		CollectionAssert.AreEqual(new[] { "d", "c", "b", "a" }, finalItems.ToArray());
	}

	private sealed class FakeFolderEnumerationSource<T>(
		IReadOnlyList<IReadOnlyCollection<T>> batches,
		IReadOnlyCollection<T> finalItems) : IFolderEnumerationSource<T>
	{
		public async Task<IReadOnlyCollection<T>> EnumerateAsync(
			Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
			CancellationToken cancellationToken)
		{
			foreach (var batch in batches)
				await publishBatchAsync(batch);

			return finalItems;
		}
	}
}
