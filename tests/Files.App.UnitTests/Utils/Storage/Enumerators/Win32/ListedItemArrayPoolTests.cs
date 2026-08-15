// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies reuse and cleanup of listed-item scratch buffers.</summary>
[TestClass]
public sealed class ListedItemArrayPoolTests
{
	/// <summary>Ensures returned buffers are reused after their references are cleared.</summary>
	[TestMethod]
	public void Return_ClearsAndReusesStandardBuffer()
	{
		var pool = new ListedItemArrayPool(preseedCount: 1);
		var buffer = pool.Rent();
		buffer[0] = new ListedItem("item");

		pool.Return(buffer);
		var reusedBuffer = pool.Rent();

		Assert.AreSame(buffer, reusedBuffer);
		Assert.IsNull(reusedBuffer[0]);
	}

	/// <summary>Ensures an empty pool allocates one fixed-size buffer on demand.</summary>
	[TestMethod]
	public void Rent_AllocatesWhenPoolIsEmpty()
	{
		var pool = new ListedItemArrayPool(preseedCount: 0);

		var buffer = pool.Rent();

		Assert.AreEqual(ListedItemArrayPool.BufferLength, buffer.Length);
		Assert.AreEqual(1, pool.RentMissCount);
	}

	/// <summary>Ensures overflow buffers are cleared but are not retained as standard buffers.</summary>
	[TestMethod]
	public void Return_DoesNotRetainOverflowBuffer()
	{
		var pool = new ListedItemArrayPool(preseedCount: 0);
		var overflowBuffer = new ListedItem[ListedItemArrayPool.BufferLength + 1];
		var retainedItem = new ListedItem("item");
		overflowBuffer[0] = retainedItem;

		pool.Return(overflowBuffer);

		Assert.AreEqual(0, pool.AvailableCount);
		Assert.IsNull(overflowBuffer[0]);
	}
}
