// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Files.App.Utils;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Reuses fixed-size scratch arrays used during legacy item materialization.</summary>
internal sealed class ListedItemArrayPool
{
	internal const int BufferLength = 256;
	internal const int DefaultPreseedCount = 16;

	/// <summary>Gets the application-wide pool for legacy listed-item buffers.</summary>
	internal static ListedItemArrayPool Shared { get; } = new(DefaultPreseedCount);

	private readonly ConcurrentBag<ListedItem[]> buffers = new();
	private long rentMissCount;

	/// <summary>Creates a pool with the requested number of preallocated buffers.</summary>
	/// <param name="preseedCount">Number of buffers to allocate before the first rent.</param>
	internal ListedItemArrayPool(int preseedCount)
	{
		if (preseedCount < 0)
			throw new ArgumentOutOfRangeException(nameof(preseedCount));

		for (var index = 0; index < preseedCount; index++)
			buffers.Add(new ListedItem[BufferLength]);
	}

	/// <summary>Gets the number of buffers currently available for reuse.</summary>
	internal int AvailableCount => buffers.Count;

	/// <summary>Gets the number of rents that required an on-demand buffer.</summary>
	internal long RentMissCount => Interlocked.Read(ref rentMissCount);

	/// <summary>Rents a scratch buffer, allocating one when the pool is empty.</summary>
	internal ListedItem[] Rent()
	{
		if (buffers.TryTake(out var buffer))
			return buffer;

		Interlocked.Increment(ref rentMissCount);
		return new ListedItem[BufferLength];
	}

	/// <summary>Clears and returns a standard-size buffer to the pool.</summary>
	/// <param name="buffer">Buffer that is no longer in use.</param>
	internal void Return(ListedItem[] buffer)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		Array.Clear(buffer);

		if (buffer.Length == BufferLength)
			buffers.Add(buffer);
	}
}
