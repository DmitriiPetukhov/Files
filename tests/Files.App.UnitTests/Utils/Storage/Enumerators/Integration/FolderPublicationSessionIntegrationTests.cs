using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Integration;

/// <summary>Verifies publication session integration with an enumeration source.</summary>
[TestClass]
public sealed class FolderPublicationSessionIntegrationTests
{
	/// <summary>Ensures source batches and final results share one publication session.</summary>
	[TestMethod]
	public async Task EnumerateAsync_UsesOnePublicationSessionForSourceBatchesAndFinalResult()
	{
		var source = new CallbackFolderEnumerationSourceStub<string>(
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
		CollectionAssert.AreEqual(new[] { "a", "b" }, publishedSnapshots[0].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, publishedSnapshots[1].ToArray());
		CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, finalSnapshot!.ToArray());
	}
}
