using System.Linq;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationSessionIntegrationTests
{
	[TestMethod]
	public async Task SourceBatchesAndFinalResultUseOnePublicationSession()
	{
		var source = new FakeFolderEnumerationSource<string>(
		[
			(IReadOnlyCollection<string>)["b", "a"],
			(IReadOnlyCollection<string>)["d", "c"]
		],
		["a", "b", "c", "d"]);
		var session = new FolderPublicationSession<string>(StringComparer.Ordinal);
		var publishedSnapshots = new List<IReadOnlyCollection<string>>();

		var finalItems = await source.EnumerateAsync(batch =>
		{
			Assert.IsTrue(session.TryAppend(batch, CancellationToken.None, out var snapshot));
			publishedSnapshots.Add(snapshot!);
			return Task.CompletedTask;
		}, CancellationToken.None);

		Assert.IsTrue(session.TryReplaceFinal(finalItems, CancellationToken.None, out var finalSnapshot));
		CollectionAssert.AreEqual(["a", "b"], publishedSnapshots[0].ToArray());
		CollectionAssert.AreEqual(["a", "b", "c", "d"], publishedSnapshots[1].ToArray());
		CollectionAssert.AreEqual(["a", "b", "c", "d"], finalSnapshot!.ToArray());
	}

	private sealed class FakeFolderEnumerationSource<T>(IReadOnlyList<IReadOnlyCollection<T>> batches, IReadOnlyCollection<T> finalItems)
		: IFolderEnumerationSource<T>
	{
		public async Task<IReadOnlyCollection<T>> EnumerateAsync(
			Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
			CancellationToken cancellationToken)
		{
			foreach (var batch in batches)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await publishBatchAsync(batch);
			}

			return finalItems;
		}
	}
}
