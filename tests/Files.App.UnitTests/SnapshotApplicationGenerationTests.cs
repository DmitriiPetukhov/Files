using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class SnapshotApplicationGenerationTests
{
	[TestMethod]
	public void NewerApplicationInvalidatesOlderApplication()
	{
		var generation = new SnapshotApplicationGeneration();
		var first = generation.Start();
		var second = generation.Start();

		Assert.IsFalse(generation.IsCurrent(first));
		Assert.IsTrue(generation.IsCurrent(second));
	}

	[TestMethod]
	public void InvalidateRejectsQueuedApplication()
	{
		var generation = new SnapshotApplicationGeneration();
		var application = generation.Start();

		generation.Invalidate();

		Assert.IsFalse(generation.IsCurrent(application));
	}
}
