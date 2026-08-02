// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

internal sealed class Win32IncrementalSortedAccumulator<T>
{
	private readonly IComparer<T> comparer;
	private ImmutableSortedSet<T> currentRoot;

	public Win32IncrementalSortedAccumulator(IComparer<T> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		this.comparer = comparer;
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

	public ImmutableSortedSet<T> Replace(IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		var builder = ImmutableSortedSet.CreateBuilder(comparer);
		builder.UnionWith(items);
		currentRoot = builder.ToImmutable();

		return currentRoot;
	}
}
