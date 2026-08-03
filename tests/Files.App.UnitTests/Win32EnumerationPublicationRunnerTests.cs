using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32EnumerationPublicationRunnerTests
{
	[TestMethod]
	public async Task RetainsEveryAcceptedItemWhenIntermediatePublicationFails()
	{
		var failures = new List<Exception>();
		await using var runner = new Win32EnumerationPublicationRunner<int>(
			_ => Task.FromException(new InvalidOperationException("publication failed")),
			initialBatchSize: 1,
			intermediateBatchSize: 1,
			batchTimeout: TimeSpan.FromSeconds(1),
			errorHandler: failures.Add);

		await runner.AddAsync(1, CancellationToken.None);
		await runner.AddAsync(2, CancellationToken.None);
		await runner.AddAsync(3, CancellationToken.None);
		var finalItems = await runner.CompleteAsync(CancellationToken.None);

		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, finalItems.ToArray());
		Assert.AreEqual(3, failures.Count);
	}
}
