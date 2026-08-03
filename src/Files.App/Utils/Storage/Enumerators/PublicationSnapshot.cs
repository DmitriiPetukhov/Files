// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Immutable;
using System.Linq;

namespace Files.App.Utils.Storage;

/// <summary>
/// Exposes one immutable sorted root through the existing read-only collection contract.
/// </summary>
/// <typeparam name="T">The published item type.</typeparam>
internal sealed class PublicationSnapshot<T>(ImmutableSortedSet<PublicationEntry<T>> root) : IReadOnlyCollection<T>
{
	private readonly ImmutableSortedSet<PublicationEntry<T>> sortedRoot = root;

	public int Count => sortedRoot.Count;

	public IEnumerator<T> GetEnumerator()
		=> sortedRoot.Select(entry => entry.Item).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator()
		=> GetEnumerator();
}
