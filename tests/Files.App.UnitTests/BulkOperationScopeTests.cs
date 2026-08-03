using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Files.App.UnitTests;

[TestClass]
public sealed class BulkOperationScopeTests
{
	[TestMethod]
	public void EndsBulkOperationWhenApplyThrows()
	{
		var began = false;
		var ended = false;

		try
		{
			using var scope = new BulkOperationScope(
				() => began = true,
				() => ended = true);

			throw new InvalidOperationException("test apply failure");
		}
		catch (InvalidOperationException)
		{
		}

		Assert.IsTrue(began);
		Assert.IsTrue(ended);
	}
}
