// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Enumerators;

/// <summary>Represents one immutable, ordered portion of a folder enumeration.</summary>
internal sealed record FolderEnumerationBatch<TItem>
{
	/// <summary>Creates a non-empty batch with its position within the enumeration.</summary>
	/// <param name="items">Items included in the batch.</param>
	/// <param name="sequenceNumber">Sequence number assigned to the batch.</param>
	public FolderEnumerationBatch(IReadOnlyCollection<TItem> items, long sequenceNumber)
	{
		ArgumentNullException.ThrowIfNull(items);
		if (items.Count == 0)
			throw new ArgumentException("A folder enumeration batch must not be empty.", nameof(items));

		Items = Array.AsReadOnly(items.ToArray());
		SequenceNumber = sequenceNumber;
	}

	/// <summary>Gets the items carried by this batch.</summary>
	public IReadOnlyList<TItem> Items { get; }

	/// <summary>Gets the batch position within its enumeration.</summary>
	public long SequenceNumber { get; }
}
