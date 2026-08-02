// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

internal sealed class Win32FolderPublicationSession<T>
{
	private readonly object syncRoot = new();
	private readonly Win32IncrementalSortedAccumulator<T> accumulator;
	private bool isActive = true;

	public Win32FolderPublicationSession(IComparer<T> comparer)
	{
		accumulator = new Win32IncrementalSortedAccumulator<T>(comparer);
	}

	public bool TryAppend(IEnumerable<T> batch, CancellationToken cancellationToken, out ImmutableSortedSet<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(batch);

		lock (syncRoot)
		{
			if (!isActive || cancellationToken.IsCancellationRequested)
			{
				snapshot = null;
				return false;
			}

			snapshot = accumulator.AddBatch(batch);
			return true;
		}
	}

	public bool TryReplaceFinal(IEnumerable<T> items, CancellationToken cancellationToken, out ImmutableSortedSet<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(items);

		lock (syncRoot)
		{
			if (!isActive || cancellationToken.IsCancellationRequested)
			{
				snapshot = null;
				return false;
			}

			snapshot = accumulator.Replace(items);
			return true;
		}
	}

	public void Cancel()
	{
		lock (syncRoot)
			isActive = false;
	}
}
