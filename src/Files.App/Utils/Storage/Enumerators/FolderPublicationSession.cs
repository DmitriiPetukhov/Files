// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.Utils.Storage;

/// <summary>
/// Owns keyed provider-neutral folder items and immutable accumulated states for one navigation.
/// </summary>
internal sealed class FolderPublicationSession : IFolderPublicationSession
{
	private readonly object syncRoot = new();
	private readonly Dictionary<FolderItemKey, FolderItem> itemsByKey = [];
	private readonly List<FolderItemKey> sourceOrder = [];
	private readonly Dictionary<FolderItemKey, long> revisions = [];
	private FolderPublicationState currentState = new(0, ImmutableArray<FolderItem>.Empty);
	private long nextVersion;
	private bool isActive = true;

	/// <inheritdoc />
	public bool TryAppend(
		FolderEnumerationBatch<FolderItem> batch,
		CancellationToken cancellationToken,
		out FolderPublicationState? state)
	{
		ArgumentNullException.ThrowIfNull(batch);

		lock (syncRoot)
		{
			if (!CanMutate(cancellationToken))
			{
				state = null;
				return false;
			}

			foreach (var item in batch.Items)
			{
				if (itemsByKey.ContainsKey(item.Key))
				{
					itemsByKey[item.Key] = item;
					revisions[item.Key]++;
				}
				else
				{
					itemsByKey.Add(item.Key, item);
					sourceOrder.Add(item.Key);
					revisions.Add(item.Key, 1);
				}
			}

			state = CreateNextState();
			return true;
		}
	}

	/// <inheritdoc />
	public bool TryApplyUpdate(
		FolderItemKey key,
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken,
		out FolderPublicationState? state)
	{
		ArgumentNullException.ThrowIfNull(item);

		lock (syncRoot)
		{
			if (!CanMutate(cancellationToken) || key != item.Key ||
				!revisions.TryGetValue(key, out var currentRevision) || currentRevision != expectedRevision)
			{
				state = null;
				return false;
			}

			itemsByKey[key] = item;
			revisions[key] = currentRevision + 1;
			state = CreateNextState();
			return true;
		}
	}

	/// <inheritdoc />
	public FolderPublicationState GetCurrentState()
	{
		lock (syncRoot)
			return currentState;
	}

	/// <inheritdoc />
	public bool TryGetRevision(FolderItemKey key, out long revision)
	{
		lock (syncRoot)
			return revisions.TryGetValue(key, out revision);
	}

	/// <inheritdoc />
	public void Complete()
	{
		lock (syncRoot)
			isActive = false;
	}

	/// <inheritdoc />
	public void Cancel()
	{
		lock (syncRoot)
			isActive = false;
	}

	private FolderPublicationState CreateNextState()
	{
		var items = ImmutableArray.CreateBuilder<FolderItem>(sourceOrder.Count);
		foreach (var key in sourceOrder)
			items.Add(itemsByKey[key]);

		currentState = new FolderPublicationState(++nextVersion, items.MoveToImmutable());
		return currentState;
	}

	private bool CanMutate(CancellationToken cancellationToken)
		=> isActive && !cancellationToken.IsCancellationRequested;
}

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
