using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies the source-owned Win32 enumeration adapter.</summary>
[TestClass]
public sealed class Win32FolderEnumerationSourceTests
{
	private const string FolderPath = @"C:\EnumerationTests";

	/// <summary>Ensures entries are emitted in order as bounded batches.</summary>
	[TestMethod]
	public async Task EnumerateAsync_YieldsOrderedFolderItemBatches()
	{
		var firstEntry = CreateFindData("item-00");
		var remainingEntries = Enumerable.Range(1, 32).Select(index => CreateFindData($"item-{index:00}"));
		var handle = new ScriptedWin32FindHandle(remainingEntries);
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, firstEntry);

		var batches = new List<FolderEnumerationBatch<FolderItem>>();
		await foreach (var batch in source.EnumerateAsync())
			batches.Add(batch);

		Assert.AreEqual(2, batches.Count);
		Assert.AreEqual(0, batches[0].SequenceNumber);
		Assert.AreEqual(1, batches[1].SequenceNumber);
		Assert.AreEqual("item-00", batches[0].Items[0].Name);
		Assert.AreEqual("item-31", batches[0].Items[^1].Name);
		Assert.AreEqual("item-32", batches[1].Items[0].Name);
		Assert.AreEqual(Path.Combine(FolderPath, "item-00"), batches[0].Items[0].Key.OpaqueId);
		Assert.AreEqual(FolderItemKind.File, batches[0].Items[0].Kind);
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures a full batch is yielded before the next native entry is requested.</summary>
	[TestMethod]
	public async Task EnumerateAsync_YieldsBatchBeforeReadingNextEntry()
	{
		var failure = new InvalidOperationException("next entry should not be requested yet");
		var remainingEntries = Enumerable.Range(1, 31).Select(index => CreateFindData($"item-{index:00}"));
		var handle = new ScriptedWin32FindHandle(remainingEntries, failure);
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item-00"));
		await using var enumerator = source.EnumerateAsync().GetAsyncEnumerator();

		Assert.IsTrue(await enumerator.MoveNextAsync());
		Assert.AreEqual(32, enumerator.Current.Items.Count);
	}

	/// <summary>Ensures cancellation releases the enumeration handle.</summary>
	[TestMethod]
	public async Task EnumerateAsync_DisposesHandleWhenCanceled()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await CaptureExceptionAsync<OperationCanceledException>(async () =>
		{
			await foreach (var _ in source.EnumerateAsync(cancellationTokenSource.Token))
			{
			}
		});

		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures native dot entries are not exposed as folder items.</summary>
	[TestMethod]
	public async Task EnumerateAsync_SkipsDotEntries()
	{
		var handle = new ScriptedWin32FindHandle(
		[
			CreateFindData("..", isDirectory: true),
			CreateFindData("item")
		]);
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData(".", isDirectory: true));

		var items = new List<FolderItem>();
		await foreach (var batch in source.EnumerateAsync())
			items.AddRange(batch.Items);

		Assert.AreEqual(1, items.Count);
		Assert.AreEqual("item", items[0].Name);
	}

	/// <summary>Ensures an empty folder completes without emitting empty batches.</summary>
	[TestMethod]
	public async Task EnumerateAsync_ReturnsNoBatchesForEmptyFolder()
	{
		var handle = new ScriptedWin32FindHandle(
		[
			CreateFindData("..", isDirectory: true)
		]);
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData(".", isDirectory: true));

		var batches = new List<FolderEnumerationBatch<FolderItem>>();
		await foreach (var batch in source.EnumerateAsync())
			batches.Add(batch);

		Assert.AreEqual(0, batches.Count);
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures cancellation after a yielded item still releases the handle.</summary>
	[TestMethod]
	public async Task EnumerateAsync_DisposesHandleWhenCanceledAfterItem()
	{
		var handle = new ScriptedWin32FindHandle([CreateFindData("item-01")]);
		using var cancellationTokenSource = new CancellationTokenSource();
		var materializedItems = 0;
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item-00"),
			materialize: (path, findData) =>
			{
				if (++materializedItems == 1)
					cancellationTokenSource.Cancel();

				return CreateFolderItem(path, findData);
			});

		await CaptureExceptionAsync<OperationCanceledException>(async () =>
		{
			await foreach (var _ in source.EnumerateAsync(cancellationTokenSource.Token))
			{
			}
		});

		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures native enumeration failures propagate unchanged.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PropagatesHandleFailure()
	{
		var failure = new InvalidOperationException("find failed");
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>(), failure);
		await using var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));

		var exception = await CaptureExceptionAsync<InvalidOperationException>(async () =>
		{
			await foreach (var _ in source.EnumerateAsync())
			{
			}
		});

		Assert.AreSame(failure, exception);
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures materialization failures propagate and release the handle.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PropagatesMaterializationFailure()
	{
		var failure = new InvalidOperationException("materialization failed");
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item"),
			materialize: (_, _) => throw failure);

		var exception = await CaptureExceptionAsync<InvalidOperationException>(async () =>
		{
			await foreach (var _ in source.EnumerateAsync())
			{
			}
		});

		Assert.AreSame(failure, exception);
		Assert.AreEqual(1, handle.DisposeCount);
	}

	/// <summary>Ensures enumeration cannot restart after source disposal.</summary>
	[TestMethod]
	public async Task EnumerateAsync_RejectsUseAfterDispose()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		await source.DisposeAsync();

		var exception = CaptureException<ObjectDisposedException>(() => _ = source.EnumerateAsync());

		Assert.AreEqual(typeof(Win32FolderEnumerationSource).FullName, exception.ObjectName);
	}

	/// <summary>Ensures a source path is required.</summary>
	[TestMethod]
	public void Constructor_RejectsMissingPath()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());

		var exception = CaptureException<ArgumentException>(() =>
			new Win32FolderEnumerationSource(null!, handle, CreateFindData("item")));

		Assert.AreEqual("path", exception.ParamName);
	}

	/// <summary>Ensures a native handle is required.</summary>
	[TestMethod]
	public void Constructor_RejectsMissingHandle()
	{
		var exception = CaptureException<ArgumentNullException>(() =>
			new Win32FolderEnumerationSource(
				FolderPath,
				null!,
				CreateFindData("item")));

		Assert.AreEqual("findHandle", exception.ParamName);
	}

	/// <summary>Ensures source creation rejects a missing folder path.</summary>
	[TestMethod]
	public void TryCreate_RejectsMissingPath()
	{
		var exception = CaptureException<ArgumentException>(() =>
			Win32FolderEnumerationSource.TryCreate(null!));

		Assert.AreEqual("path", exception.ParamName);
	}

	/// <summary>Ensures source disposal is idempotent.</summary>
	[TestMethod]
	public async Task DisposeAsync_DisposesEnumerationHandleOnce()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));

		await source.DisposeAsync();
		await source.DisposeAsync();

		Assert.AreEqual(1, handle.DisposeCount);
	}

	private static Win32PInvoke.WIN32_FIND_DATA CreateFindData(string name, bool isDirectory = false)
		=> new()
		{
			cFileName = name,
			dwFileAttributes = isDirectory ? (uint)FileAttributes.Directory : 0u,
		};

	private static FolderItem? CreateFolderItem(string path, Win32PInvoke.WIN32_FIND_DATA findData)
	{
		if (findData.cFileName is "." or "..")
			return null;

		var isFolder = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != 0;
		return new FolderItem(
			new FolderItemKey("win32", Path.GetFullPath(Path.Combine(path, findData.cFileName))),
			findData.cFileName,
			isFolder ? FolderItemKind.Folder : FolderItemKind.File,
			null,
			null);
	}

	private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action)
		where TException : Exception
	{
		try
		{
			await action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null;
	}

	private static TException CaptureException<TException>(Action action)
		where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null;
	}
}
