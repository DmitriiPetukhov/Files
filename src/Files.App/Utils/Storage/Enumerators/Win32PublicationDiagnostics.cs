// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Files.App.Utils.Storage;

internal readonly record struct FolderPublicationDiagnosticEvent(
	string Phase,
	int SessionSequence,
	int PublicationSequence,
	int PayloadCount,
	int AccumulatedCount,
	int PrimaryCount,
	long ElapsedMilliseconds);

internal sealed class FolderPublicationDiagnostics
{
	private static int nextSessionSequence;
	private readonly Action<FolderPublicationDiagnosticEvent>? eventSink;
	private readonly long startedTimestamp = Stopwatch.GetTimestamp();
	private int publicationSequence;

	public FolderPublicationDiagnostics(Action<FolderPublicationDiagnosticEvent>? eventSink = null, int? sessionSequence = null)
	{
		this.eventSink = eventSink;
		SessionSequence = sessionSequence ?? Interlocked.Increment(ref nextSessionSequence);
	}

	public int SessionSequence { get; }

	public void Debug(string phase, int payloadCount, int accumulatedCount, int primaryCount)
		=> Record(phase, payloadCount, accumulatedCount, primaryCount, null);

	public void Warning(string phase, int payloadCount, int accumulatedCount, int primaryCount, Exception exception)
		=> Record(phase, payloadCount, accumulatedCount, primaryCount, exception);

	private void Record(string phase, int payloadCount, int accumulatedCount, int primaryCount, Exception? exception)
	{
		var diagnosticEvent = new FolderPublicationDiagnosticEvent(
			phase,
			SessionSequence,
			Interlocked.Increment(ref publicationSequence),
			payloadCount,
			accumulatedCount,
			primaryCount,
			(long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);

		if (eventSink is not null)
		{
			eventSink(diagnosticEvent);
			return;
		}

		if (exception is null)
		{
			App.Logger.LogDebug(
				"Folder publication phase={Phase} session={SessionSequence} publication={PublicationSequence} payload={PayloadCount} accumulated={AccumulatedCount} primary={PrimaryCount} elapsedMs={ElapsedMilliseconds}",
				diagnosticEvent.Phase,
				diagnosticEvent.SessionSequence,
				diagnosticEvent.PublicationSequence,
				diagnosticEvent.PayloadCount,
				diagnosticEvent.AccumulatedCount,
				diagnosticEvent.PrimaryCount,
				diagnosticEvent.ElapsedMilliseconds);
		}
		else
		{
			App.Logger.LogWarning(
				exception,
				"Folder publication phase={Phase} session={SessionSequence} publication={PublicationSequence} payload={PayloadCount} accumulated={AccumulatedCount} primary={PrimaryCount} elapsedMs={ElapsedMilliseconds}",
				diagnosticEvent.Phase,
				diagnosticEvent.SessionSequence,
				diagnosticEvent.PublicationSequence,
				diagnosticEvent.PayloadCount,
				diagnosticEvent.AccumulatedCount,
				diagnosticEvent.PrimaryCount,
				diagnosticEvent.ElapsedMilliseconds);
		}
	}
}
