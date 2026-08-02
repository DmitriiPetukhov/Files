// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

internal sealed class Win32IncrementalSortedAccumulator<T>
{
	private ImmutableSortedSet<T> currentRoot;

	public Win32IncrementalSortedAccumulator(IComparer<T> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		currentRoot = ImmutableSortedSet.Create(comparer);
	}

	public ImmutableSortedSet<T> AddBatch(IEnumerable<T> batch)
	{
		ArgumentNullException.ThrowIfNull(batch);

		var builder = currentRoot.ToBuilder();
		builder.UnionWith(batch);
		currentRoot = builder.ToImmutable();

		return currentRoot;
	}
}
