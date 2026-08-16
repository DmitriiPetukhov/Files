using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies bounded cancellation and cleanup for Win32 folder opens.</summary>
[TestClass]
public sealed class Win32FolderOpenOperationTests
{
	private TemporaryTestDirectory temporaryDirectory = null!;

	private string FolderPath => temporaryDirectory.DirectoryPath;

	/// <summary>Creates an isolated directory for the test.</summary>
	[TestInitialize]
	public void CreateTemporaryDirectory()
		=> temporaryDirectory = new TemporaryTestDirectory();

	/// <summary>Removes the isolated directory after the test.</summary>
	[TestCleanup]
	public void CleanupTemporaryDirectory()
		=> temporaryDirectory.Dispose();

	/// <summary>Ensures cancellation returns before a blocked open completes and disposes a late source.</summary>
	[TestMethod]
	public async Task TryOpenAsync_ReturnsCanceledBeforeOpenCompletesAndDisposesLateSource()
	{
		var opener = new BlockingWin32FolderOpener();
		var sourceDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handle = new ScriptedWin32FindHandle(
			Array.Empty<Win32PInvoke.WIN32_FIND_DATA>(),
			onDispose: sourceDisposed.SetResult);
		var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		var operation = new Win32FolderOpenOperation(opener, (_, _) => { });
		using var cancellationTokenSource = new CancellationTokenSource();

		var openTask = operation.TryOpenAsync(FolderPath, cancellationTokenSource.Token);
		cancellationTokenSource.Cancel();

		var result = await openTask.WaitAsync(TimeSpan.FromSeconds(1));

		Assert.AreEqual(Win32FolderEnumerationOpenStatus.Canceled, result.Status);
		Assert.IsFalse(sourceDisposed.Task.IsCompleted);

		opener.Complete(new Win32FolderEnumerationOpenResult(
			Win32FolderEnumerationOpenStatus.Opened,
			source,
			null,
			0));

		await sourceDisposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
		Assert.AreEqual(1, handle.DisposeCount);
	}

	private static Win32PInvoke.WIN32_FIND_DATA CreateFindData(string name)
		=> new()
		{
			cFileName = name,
		};
}
