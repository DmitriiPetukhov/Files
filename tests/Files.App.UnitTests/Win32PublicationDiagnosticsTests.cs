using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32PublicationDiagnosticsTests
{
	[TestMethod]
	public void RecordsSafePublicationPhasesAndCounters()
	{
		var events = new List<Win32PublicationDiagnosticEvent>();
		var diagnostics = new Win32PublicationDiagnostics(events.Add, sessionSequence: 17);
		var session = new Win32FolderPublicationSession<int>(Comparer<int>.Default, diagnostics: diagnostics);

		Assert.IsTrue(session.TryAppend(new[] { 1, 2 }, CancellationToken.None, out _));
		Assert.IsTrue(session.TryReplaceFinal(new[] { 3, 4 }, CancellationToken.None, out _));
		diagnostics.Debug("coalesced", payloadCount: 2, accumulatedCount: 2, primaryCount: 2);
		diagnostics.Debug("stale", payloadCount: 0, accumulatedCount: 2, primaryCount: 2);
		diagnostics.Debug("rejected", payloadCount: 0, accumulatedCount: 2, primaryCount: 2);
		diagnostics.Warning("failed", payloadCount: 0, accumulatedCount: 2, primaryCount: 2, new InvalidOperationException("failure"));

		CollectionAssert.AreEqual(new[] { "first", "final", "coalesced", "stale", "rejected", "failed" }, events.Select(x => x.Phase).ToArray());
		Assert.IsTrue(events.All(x => x.SessionSequence == 17));
		Assert.IsTrue(events.All(x => x.PayloadCount >= 0 && x.AccumulatedCount >= 0 && x.PrimaryCount >= 0 && x.ElapsedMilliseconds >= 0));
		Assert.IsTrue(events.All(x => !x.ToString().Contains("C:\\", StringComparison.OrdinalIgnoreCase)));
	}
}
