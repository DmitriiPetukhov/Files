using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using OVERLAPPED = Files.App.Helpers.Win32PInvoke.OVERLAPPED;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32FolderChangeSourceTests
{
	[TestMethod]
	public void Parse_ReturnsOrderedNotificationsWithFullPaths()
	{
		var buffer = CreateBuffer(
			(Win32FolderChangeAction.Added, "one.txt"),
			(Win32FolderChangeAction.RenamedOldName, "old.txt"),
			(Win32FolderChangeAction.RenamedNewName, "new.txt"));

		var notifications = Win32FolderChangeParser.Parse(buffer, @"C:\Folder");

		Assert.AreEqual(3, notifications.Count);
		Assert.AreEqual(Win32FolderChangeAction.Added, notifications[0].Action);
		Assert.AreEqual(@"C:\Folder\one.txt", notifications[0].FullPath);
		Assert.AreEqual(Win32FolderChangeAction.RenamedOldName, notifications[1].Action);
		Assert.AreEqual(@"C:\Folder\old.txt", notifications[1].FullPath);
		Assert.AreEqual(Win32FolderChangeAction.RenamedNewName, notifications[2].Action);
		Assert.AreEqual(@"C:\Folder\new.txt", notifications[2].FullPath);
	}

	[TestMethod]
	public void Parse_RejectsInvalidRecordOffset()
	{
		var buffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 1);

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	[TestMethod]
	public void Parse_RejectsTruncatedFileName()
	{
		var buffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), uint.MaxValue);

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	[TestMethod]
	public void Parse_RejectsTruncatedHeader()
		=> AssertFormatException(() => Win32FolderChangeParser.Parse(new byte[11], @"C:\Folder"));

	[TestMethod]
	public void Parse_RejectsTruncatedSubsequentRecord()
	{
		var buffer = CreateBuffer(
			(Win32FolderChangeAction.Added, "one.txt"),
			(Win32FolderChangeAction.Removed, "two.txt"));
		Array.Resize(ref buffer, 36);

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	[TestMethod]
	public void Parse_RejectsZeroOrOddFileNameLengths()
	{
		var zeroLengthBuffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(zeroLengthBuffer.AsSpan(8, 4), 0);

		var oddLengthBuffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(oddLengthBuffer.AsSpan(8, 4), 1);

		AssertFormatException(() => Win32FolderChangeParser.Parse(zeroLengthBuffer, @"C:\Folder"));
		AssertFormatException(() => Win32FolderChangeParser.Parse(oddLengthBuffer, @"C:\Folder"));
	}

	[TestMethod]
	public void Parse_RejectsFileNameCrossingRecordBoundary()
	{
		var buffer = CreateBuffer(
			(Win32FolderChangeAction.Added, "one.txt"),
			(Win32FolderChangeAction.Removed, "two.txt"));
		var firstRecordLength = AlignToFourBytes(12 + Encoding.Unicode.GetByteCount("one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), (uint)(firstRecordLength - 12 + 2));

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	[TestMethod]
	public async Task WatchAsync_PropagatesCallbackFailure()
	{
		var folderPath = Path.Combine(Path.GetTempPath(), $"Files-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folderPath);

		try
		{
			using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var source = new Win32FolderChangeSource(folderPath, includeAttributes: false);
			var watchTask = source.WatchAsync(
				_ => throw new InvalidOperationException("Sentinel callback failure."),
				cancellationSource.Token);

			await Task.Delay(100);
			File.WriteAllText(Path.Combine(folderPath, "trigger.txt"), "trigger");

			try
			{
				await watchTask;
				Assert.Fail("Expected the watcher task to propagate the callback failure.");
			}
			catch (InvalidOperationException)
			{
			}
		}
		finally
		{
			if (Directory.Exists(folderPath))
				Directory.Delete(folderPath, recursive: true);
		}
	}

	[TestMethod]
	public async Task WatchAsync_ContinuesAfterPendingRead()
	{
		using var cancellationSource = new CancellationTokenSource();
		var native = new FakeNative
		{
			CompletionBuffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"))
		};
		IReadOnlyCollection<Win32FolderChangeNotification>? receivedNotifications = null;
		var source = new Win32FolderChangeSource(@"C:\Folder", includeAttributes: false, native);

		await source.WatchAsync(notifications =>
		{
			receivedNotifications = notifications;
			cancellationSource.Cancel();
		}, cancellationSource.Token);

		Assert.IsNotNull(receivedNotifications);
		Assert.AreEqual(1, receivedNotifications.Count);
		Assert.AreEqual(@"C:\Folder\one.txt", receivedNotifications.First().FullPath);
		Assert.AreEqual(1, native.ReadCallCount);
	}

	[TestMethod]
	public async Task WatchAsync_KeepsNativeBufferStableUntilCompletion()
	{
		using var cancellationSource = new CancellationTokenSource();
		var native = new FakeNative
		{
			CompletionBuffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"))
		};
		var source = new Win32FolderChangeSource(@"C:\Folder", includeAttributes: false, native);

		await source.WatchAsync(_ => cancellationSource.Cancel(), cancellationSource.Token);

		Assert.AreNotEqual(IntPtr.Zero, native.SubmittedBuffer);
		Assert.AreEqual(native.SubmittedBuffer, native.CompletedBuffer);
	}

	[TestMethod]
	public async Task WatchAsync_CancellationCleansUpWithoutCallback()
	{
		using var cancellationSource = new CancellationTokenSource();
		var native = new FakeNative { WaitForCancellation = true };
		var callbackCalled = false;
		var source = new Win32FolderChangeSource(@"C:\Folder", includeAttributes: false, native);
		var watchTask = source.WatchAsync(_ => callbackCalled = true, cancellationSource.Token);

		Assert.IsTrue(native.ReadSubmitted.Wait(TimeSpan.FromSeconds(5)));
		cancellationSource.Cancel();
		await watchTask;

		Assert.IsFalse(callbackCalled);
		Assert.AreEqual(1, native.CancelCallCount);
		Assert.AreEqual(1, native.CloseHandleCallCount);
	}

	[TestMethod]
	public async Task WatchAsync_NativeFailureCleansUpAndFaults()
	{
		using var cancellationSource = new CancellationTokenSource();
		var native = new FakeNative { ReadErrorCode = 5 };
		var source = new Win32FolderChangeSource(@"C:\Folder", includeAttributes: false, native);

		try
		{
			await source.WatchAsync(_ => { }, cancellationSource.Token);
			Assert.Fail("Expected the watcher task to propagate the native failure.");
		}
		catch (Win32Exception exception)
		{
			Assert.AreEqual(5, exception.NativeErrorCode);
		}

		Assert.AreEqual(1, native.CancelCallCount);
		Assert.AreEqual(1, native.CloseHandleCallCount);
	}

	[TestMethod]
	public async Task FolderChangeQueueGate_ClosesAfterInFlightPublish()
	{
		using var cancellationSource = new CancellationTokenSource();
		var gate = new FolderChangeQueueGate();
		var generation = gate.CaptureGeneration();
		var queue = new ConcurrentQueue<int>();
		var publishStarted = new ManualResetEventSlim();
		var releasePublish = new ManualResetEventSlim();

		var publishTask = Task.Run(() => gate.TryRun(generation, cancellationSource.Token, () =>
		{
			publishStarted.Set();
			releasePublish.Wait();
			queue.Enqueue(1);
		}));

		Assert.IsTrue(publishStarted.Wait(TimeSpan.FromSeconds(5)));
		var closeTask = Task.Run(() => gate.Close(cancellationSource, queue.Clear));
		releasePublish.Set();

		await Task.WhenAll(publishTask, closeTask);

		Assert.IsTrue(queue.IsEmpty);
		Assert.IsFalse(gate.TryRun(generation, cancellationSource.Token, () => queue.Enqueue(2)));
	}

	private static byte[] CreateBuffer(params (Win32FolderChangeAction Action, string Name)[] records)
	{
		var encodedNames = new List<byte[]>();
		var totalLength = 0;

		foreach (var record in records)
		{
			var encodedName = Encoding.Unicode.GetBytes(record.Name);
			encodedNames.Add(encodedName);
			totalLength += AlignToFourBytes(12 + encodedName.Length);
		}

		var buffer = new byte[totalLength];
		var offset = 0;
		for (var index = 0; index < records.Length; index++)
		{
			var recordLength = AlignToFourBytes(12 + encodedNames[index].Length);
			var nextOffset = index == records.Length - 1 ? 0 : recordLength;

			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), (uint)nextOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4, 4), (uint)records[index].Action);
			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 8, 4), (uint)encodedNames[index].Length);
			encodedNames[index].CopyTo(buffer, offset + 12);
			offset += recordLength;
		}

		return buffer;
	}

	private static int AlignToFourBytes(int value)
		=> (value + 3) & ~3;

	private static void AssertFormatException(Action action)
	{
		try
		{
			action();
			Assert.Fail("Expected a FormatException.");
		}
		catch (FormatException)
		{
		}
	}

	private sealed class FakeNative : IWin32FolderChangeNative
	{
		public byte[] CompletionBuffer { get; init; } = [];
		public int? ReadErrorCode { get; init; }
		public bool WaitForCancellation { get; init; }
		public ManualResetEventSlim ReadSubmitted { get; } = new();
		public int ReadCallCount { get; private set; }
		public int CancelCallCount { get; private set; }
		public int CloseHandleCallCount { get; private set; }
		public IntPtr SubmittedBuffer { get; private set; }
		public IntPtr CompletedBuffer { get; private set; }
		private ManualResetEventSlim CancellationRequested { get; } = new();

		public IntPtr CreateWatchHandle(string path)
			=> new(1);

		public SafeFileHandle CreateEvent()
			=> new(new IntPtr(2), ownsHandle: false);

		public bool ReadDirectoryChanges(
			IntPtr watchHandle,
			IntPtr buffer,
			int bufferLength,
			int notifyFilters,
			ref OVERLAPPED overlapped,
			out int errorCode)
		{
			ReadCallCount++;
			ReadSubmitted.Set();
			SubmittedBuffer = buffer;
			if (ReadErrorCode is int readErrorCode)
			{
				errorCode = readErrorCode;
				return false;
			}

			Marshal.Copy(CompletionBuffer, 0, buffer, CompletionBuffer.Length);
			errorCode = 997;
			return false;
		}

		public uint WaitForSingleObjectEx(IntPtr eventHandle, uint timeout, bool alertable, out int errorCode)
		{
			if (WaitForCancellation)
				CancellationRequested.Wait(TimeSpan.FromSeconds(5));

			errorCode = 0;
			return 0;
		}

		public bool GetOverlappedResult(
			IntPtr watchHandle,
			ref OVERLAPPED overlapped,
			out uint bytesTransferred,
			bool wait,
			out int errorCode)
		{
			CompletedBuffer = SubmittedBuffer;
			bytesTransferred = (uint)CompletionBuffer.Length;
			errorCode = 0;
			return true;
		}

		public void CancelIoEx(IntPtr watchHandle)
		{
			CancelCallCount++;
			CancellationRequested.Set();
		}

		public void CloseHandle(IntPtr watchHandle)
		{
			CloseHandleCallCount++;
		}
	}
}
