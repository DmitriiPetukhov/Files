using System;
using System.Threading.Tasks;
using Files.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class AppLifecycleTests
{
	[TestMethod]
	public async Task DisposeHostAsync_DisposesOwnedHost()
	{
		var host = new TrackingAsyncDisposable();

		await App.DisposeHostAsync(host);

		Assert.AreEqual(1, host.DisposeCount);
	}

	private sealed class TrackingAsyncDisposable : IAsyncDisposable
	{
		public int DisposeCount { get; private set; }

		public ValueTask DisposeAsync()
		{
			DisposeCount++;
			return ValueTask.CompletedTask;
		}
	}
}
