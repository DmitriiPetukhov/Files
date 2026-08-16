using System.Collections.Generic;
using System.Threading;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;

/// <summary>Blocks comparisons until the caller releases the comparer.</summary>
internal sealed class BlockingComparer(ManualResetEventSlim entered, ManualResetEventSlim release) : Comparer<int>
{
	/// <summary>Signals entry and waits before comparing values.</summary>
	public override int Compare(int x, int y)
	{
		entered.Set();
		release.Wait();
		return y.CompareTo(x);
	}
}
