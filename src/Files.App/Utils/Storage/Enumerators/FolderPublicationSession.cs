// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

/// <summary>
/// Owns canonical items and an incrementally maintained ordered index for one navigation.
/// </summary>
/// <typeparam name="T">The published item type.</typeparam>
internal sealed class FolderPublicationSession<T> : IFolderPublicationSession<T>
{
	private readonly object syncRoot = new();
	private readonly List<PublicationEntry<T>> canonicalEntries = [];
	private IComparer<T> itemComparer;
	private ImmutableSortedSet<PublicationEntry<T>> orderedRoot;
	private long nextSequence;
	private bool isActive = true;

	public FolderPublicationSession(IComparer<T> itemComparer)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);

		this.itemComparer = itemComparer;
		orderedRoot = CreateEmptyRoot(itemComparer);
	}

	public bool TryAppend(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(batch);

		lock (syncRoot)
		{
			if (!CanPublish(cancellationToken))
			{
				snapshot = null;
				return false;
			}

			var entries = new List<PublicationEntry<T>>(batch.Count);
			foreach (var item in batch)
			{
				var entry = new PublicationEntry<T>(item, nextSequence++);
				canonicalEntries.Add(entry);
				entries.Add(entry);
			}

			var builder = orderedRoot.ToBuilder();
			builder.UnionWith(entries);
			orderedRoot = builder.ToImmutable();
			snapshot = new PublicationSnapshot<T>(orderedRoot);
			return true;
		}
	}

	public bool TryReplaceFinal(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(items);

		lock (syncRoot)
		{
			if (!CanPublish(cancellationToken))
			{
				snapshot = null;
				return false;
			}

			canonicalEntries.Clear();
			nextSequence = 0;
			var builder = CreateEmptyRoot(itemComparer).ToBuilder();
			foreach (var item in items)
			{
				var entry = new PublicationEntry<T>(item, nextSequence++);
				canonicalEntries.Add(entry);
				builder.Add(entry);
			}

			orderedRoot = builder.ToImmutable();
			snapshot = new PublicationSnapshot<T>(orderedRoot);
			return true;
		}
	}

	public bool TryRebuildIndex(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken,
		out IReadOnlyCollection<T>? snapshot)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);

		lock (syncRoot)
		{
			if (!CanPublish(cancellationToken))
			{
				snapshot = null;
				return false;
			}

			var builder = CreateEmptyRoot(itemComparer).ToBuilder();
			builder.UnionWith(canonicalEntries);
			var rebuiltRoot = builder.ToImmutable();
			this.itemComparer = itemComparer;
			orderedRoot = rebuiltRoot;
			snapshot = new PublicationSnapshot<T>(orderedRoot);
			return true;
		}
	}

	public void Cancel()
	{
		lock (syncRoot)
			isActive = false;
	}

	private bool CanPublish(CancellationToken cancellationToken)
		=> isActive && !cancellationToken.IsCancellationRequested;

	private static ImmutableSortedSet<PublicationEntry<T>> CreateEmptyRoot(IComparer<T> itemComparer)
		=> ImmutableSortedSet<PublicationEntry<T>>.Empty.WithComparer(new PublicationEntryComparer<T>(itemComparer));

	private sealed class PublicationEntryComparer<TItem>(IComparer<TItem> itemComparer) : IComparer<PublicationEntry<TItem>>
	{
		public int Compare(PublicationEntry<TItem>? x, PublicationEntry<TItem>? y)
		{
			if (ReferenceEquals(x, y))
				return 0;

			if (x is null)
				return -1;

			if (y is null)
				return 1;

			var itemComparison = itemComparer.Compare(x.Item, y.Item);
			return itemComparison != 0
				? itemComparison
				: x.Sequence.CompareTo(y.Sequence);
		}
	}
}
