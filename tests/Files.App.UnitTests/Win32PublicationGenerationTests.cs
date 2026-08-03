using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32PublicationGenerationTests
{
	[TestMethod]
	public void CompletingOldGenerationDoesNotDeactivateNewGeneration()
	{
		var generations = new Win32PublicationGeneration();
		var firstGeneration = generations.Start();
		var secondGeneration = generations.Start();

		generations.Complete(firstGeneration);

		Assert.IsTrue(generations.IsActive);
		Assert.IsTrue(generations.IsCurrent(secondGeneration));

		generations.Complete(secondGeneration);

		Assert.IsFalse(generations.IsActive);
	}
}
