using System;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators;

/// <summary>Verifies serialized publication operations.</summary>
[TestClass]
public sealed class FolderPublicationOperationGateTests
{
	/// <summary>Ensures concurrent operations execute one at a time.</summary>
	[TestMethod]
	public async Task ExecuteAsync_SerializesPublicationOperations()
	{
		using var firstEntered = new ManualResetEventSlim();
		using var releaseFirst = new ManualResetEventSlim();
		using var secondEntered = new ManualResetEventSlim();
		var gate = new FolderPublicationOperationGate();

		try
		{
			var first = Task.Run(() => gate.ExecuteAsync(async () =>
			{
				firstEntered.Set();
				releaseFirst.Wait();
				await Task.CompletedTask;
				return 1;
			}));

			Assert.IsTrue(firstEntered.Wait(TimeSpan.FromSeconds(5)));

			var second = Task.Run(() => gate.ExecuteAsync(async () =>
			{
				secondEntered.Set();
				await Task.CompletedTask;
				return 2;
			}));

			Assert.IsFalse(secondEntered.IsSet);
			releaseFirst.Set();

			Assert.AreEqual(1, await first);
			Assert.AreEqual(2, await second);
			Assert.IsTrue(secondEntered.IsSet);
		}
		finally
		{
			releaseFirst.Set();
		}
	}
}
