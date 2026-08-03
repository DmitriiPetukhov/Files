using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class FolderPublicationSessionWorkerTests
{
	[TestMethod]
	public async Task RebuildIndexAsync_RunsWithoutBlockingCaller()
	{
		var session = new FolderPublicationSession<int>(Comparer<int>.Default);
		session.TryAppend([1, 2, 3], CancellationToken.None, out _);

		using var comparerEntered = new ManualResetEventSlim();
		using var releaseComparer = new ManualResetEventSlim();
		var comparer = new BlockingComparer(comparerEntered, releaseComparer);

		try
		{
			var rebuildTask = FolderPublicationSessionWorker.RebuildIndexAsync(session, comparer, CancellationToken.None);

			Assert.IsTrue(comparerEntered.Wait(TimeSpan.FromSeconds(5)));
			Assert.IsFalse(rebuildTask.IsCompleted);

			releaseComparer.Set();
			var result = await rebuildTask;

			Assert.IsTrue(result.Accepted);
		}
		finally
		{
			releaseComparer.Set();
		}
	}

	private sealed class BlockingComparer(ManualResetEventSlim entered, ManualResetEventSlim release) : Comparer<int>
	{
		public override int Compare(int x, int y)
		{
			entered.Set();
			release.Wait();
			return y.CompareTo(x);
		}
	}
}
