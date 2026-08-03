// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

internal sealed class Win32FolderPublicationSession<T>
{
	private readonly object syncRoot = new();
	// Immutable roots can cross the worker-to-dispatcher boundary safely. The lock protects
	// the current root and counters while a new root is being built from a batch.
	private readonly Win32IncrementalSortedAccumulator<T> accumulator;
	private readonly Func<T, bool>? countsTowardPrimary;
	private readonly Win32PublicationDiagnostics? diagnostics;
	private bool isActive = true;
	private int accumulatedCount;
	private int primaryCount;
	private int publicationCount;

	public Win32FolderPublicationSession(IComparer<T> comparer, Func<T, bool>? countsTowardPrimary = null, Win32PublicationDiagnostics? diagnostics = null)
	{
		accumulator = new Win32IncrementalSortedAccumulator<T>(comparer);
		this.countsTowardPrimary = countsTowardPrimary;
		this.diagnostics = diagnostics;
	}

	public bool TryAppend(IReadOnlyCollection<T> batch, CancellationToken cancellationToken, out ImmutableSortedSet<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(batch);

		lock (syncRoot)
		{
			if (!isActive)
			{
				snapshot = null;
				diagnostics?.Debug("rejected", 0, accumulatedCount, primaryCount);
				return false;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				snapshot = null;
				diagnostics?.Debug("stale", 0, accumulatedCount, primaryCount);
				return false;
			}

			snapshot = accumulator.AddBatch(batch);
			accumulatedCount = snapshot.Count;
			primaryCount += countsTowardPrimary is null ? batch.Count : batch.Count(countsTowardPrimary);
			diagnostics?.Debug(publicationCount++ == 0 ? "first" : "intermediate", batch.Count, accumulatedCount, primaryCount);
			return true;
		}
	}

	public bool TryReplaceFinal(IReadOnlyCollection<T> items, CancellationToken cancellationToken, out ImmutableSortedSet<T>? snapshot)
		=> TryReplaceFinal(items, finalComparer: null, cancellationToken, out snapshot);

	public bool TryReplaceFinal(IReadOnlyCollection<T> items, IComparer<T> finalComparer, CancellationToken cancellationToken, out ImmutableSortedSet<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(items);

		lock (syncRoot)
		{
			if (!isActive)
			{
				snapshot = null;
				diagnostics?.Debug("rejected", 0, accumulatedCount, primaryCount);
				return false;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				snapshot = null;
				diagnostics?.Debug("stale", 0, accumulatedCount, primaryCount);
				return false;
			}

			// Intermediate publication is best effort, so rebuild from the complete final list
			// instead of assuming every detached batch reached the UI.
			snapshot = finalComparer is null ? accumulator.Replace(items) : accumulator.Replace(items, finalComparer);
			accumulatedCount = snapshot.Count;
			primaryCount = countsTowardPrimary is null ? items.Count : items.Count(countsTowardPrimary);
			publicationCount++;
			diagnostics?.Debug("final", items.Count, accumulatedCount, primaryCount);
			return true;
		}
	}

	public (int AccumulatedCount, int PrimaryCount) GetCounts()
	{
		lock (syncRoot)
			return (accumulatedCount, primaryCount);
	}

	public void Cancel()
	{
		lock (syncRoot)
			isActive = false;
	}
}
