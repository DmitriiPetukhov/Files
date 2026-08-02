using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Files.App.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class ListedItemTests
{
	[TestMethod]
	public async Task TrySetPreloadedIconData_FirstNonNullWriterWins()
	{
		var item = (ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem));
		var values = Enumerable.Range(0, 16)
			.Select(index => new byte[] { (byte)index })
			.ToArray();

		var results = await Task.WhenAll(values.Select(value => Task.Run(() => item.TrySetPreloadedIconData(value))));

		Assert.AreEqual(1, results.Count(result => result));
		Assert.IsNotNull(item.PreloadedIconData);
		Assert.IsFalse(item.TrySetPreloadedIconData(null));
		CollectionAssert.AreEqual(item.PreloadedIconData, values.Single(value => ReferenceEquals(value, item.PreloadedIconData)));
	}
}
