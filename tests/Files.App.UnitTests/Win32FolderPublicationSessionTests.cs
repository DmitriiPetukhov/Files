using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32FolderPublicationSessionTests
{
	[TestMethod]
	public void AppendsSortedBatchesAndReplacesFinalRoot()
	{
		var session = new Win32FolderPublicationSession<int>(Comparer<int>.Default);

		Assert.IsTrue(session.TryAppend(new[] { 3, 1 }, CancellationToken.None, out var firstRoot));
		CollectionAssert.AreEqual(new[] { 1, 3 }, firstRoot!.ToArray());

		Assert.IsTrue(session.TryReplaceFinal(new[] { 4, 2 }, CancellationToken.None, out var finalRoot));
		CollectionAssert.AreEqual(new[] { 2, 4 }, finalRoot!.ToArray());
	}

	[TestMethod]
	public void CancellationRejectsNewBatchesAndDoesNotChangeRoot()
	{
		var session = new Win32FolderPublicationSession<int>(Comparer<int>.Default);
		Assert.IsTrue(session.TryAppend(new[] { 1 }, CancellationToken.None, out _));

		session.Cancel();

		Assert.IsFalse(session.TryAppend(new[] { 2 }, CancellationToken.None, out var rejectedRoot));
		Assert.IsNull(rejectedRoot);
		Assert.IsFalse(session.TryReplaceFinal(new[] { 3 }, CancellationToken.None, out var rejectedFinalRoot));
		Assert.IsNull(rejectedFinalRoot);
	}
}
