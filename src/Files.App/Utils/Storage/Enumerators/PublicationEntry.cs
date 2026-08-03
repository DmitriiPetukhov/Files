// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Associates an item with a stable insertion sequence for sorted publication.
/// </summary>
/// <typeparam name="T">The published item type.</typeparam>
internal sealed class PublicationEntry<T>(T item, long sequence)
{
	public T Item { get; } = item;

	public long Sequence { get; } = sequence;
}
