// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Helpers;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies current-item resolution by the Win32 enumeration source.</summary>
[TestClass]
public sealed class Win32FolderEnumerationSourceResolutionTests
{
	private const string FolderPath = @"C:\EnumerationTests";

	/// <summary>Ensures resolution uses a fresh lookup and returns current metadata.</summary>
	[TestMethod]
	public async Task ResolveAsync_UsesFreshLookupForCurrentItem()
	{
		var enumerationHandle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var resolutionHandle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var resolvedPath = Path.Combine(FolderPath, "item");
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			enumerationHandle,
			CreateFindData("initial"),
			_ => (resolutionHandle, CreateFindData("item", isDirectory: true)));

		var item = await source.ResolveAsync(new FolderItemKey("win32", resolvedPath));

		Assert.IsNotNull(item);
		Assert.AreEqual("item", item.Name);
		Assert.AreEqual(FolderItemKind.Folder, item.Kind);
		Assert.AreEqual(resolvedPath, item.Key.OpaqueId);
		Assert.AreEqual(1, resolutionHandle.DisposeCount);
		Assert.AreEqual(0, enumerationHandle.DisposeCount);
	}

	/// <summary>Ensures missing items resolve to null without leaking the lookup handle.</summary>
	[TestMethod]
	public async Task ResolveAsync_ReturnsNullWhenLookupIsMissing()
	{
		var enumerationHandle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			enumerationHandle,
			CreateFindData("initial"),
			_ => null);

		var item = await source.ResolveAsync(new FolderItemKey("win32", Path.Combine(FolderPath, "missing")));

		Assert.IsNull(item);
	}

	/// <summary>Ensures materialization failures propagate and temporary handles are released.</summary>
	[TestMethod]
	public async Task ResolveAsync_PropagatesMaterializationFailure()
	{
		var failure = new InvalidOperationException("materialization failed");
		var enumerationHandle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var resolutionHandle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			enumerationHandle,
			CreateFindData("initial"),
			_ => (resolutionHandle, CreateFindData("item")),
			materialize: (_, _) => throw failure);

		var exception = await CaptureExceptionAsync<InvalidOperationException>(async () =>
		{
			await source.ResolveAsync(new FolderItemKey("win32", Path.Combine(FolderPath, "item")));
		});

		Assert.AreSame(failure, exception);
		Assert.AreEqual(1, resolutionHandle.DisposeCount);
		Assert.AreEqual(0, enumerationHandle.DisposeCount);
	}

	/// <summary>Ensures resolution rejects keys from another provider.</summary>
	[TestMethod]
	public async Task ResolveAsync_RejectsForeignProviderKey()
	{
		var lookupCalls = 0;
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item"),
			_ =>
			{
				lookupCalls++;
				return null;
			});

		await CaptureExceptionAsync<ArgumentException>(async () =>
		{
			await source.ResolveAsync(new FolderItemKey("other", Path.Combine(FolderPath, "item")));
		});

		Assert.AreEqual(0, lookupCalls);
	}

	/// <summary>Ensures resolution rejects keys outside the source folder.</summary>
	[TestMethod]
	public async Task ResolveAsync_RejectsItemOutsideSourceFolder()
	{
		var lookupCalls = 0;
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item"),
			_ =>
			{
				lookupCalls++;
				return null;
			});

		await CaptureExceptionAsync<ArgumentException>(async () =>
		{
			await source.ResolveAsync(new FolderItemKey("win32", Path.Combine(FolderPath, "nested", "item")));
		});

		Assert.AreEqual(0, lookupCalls);
	}

	/// <summary>Ensures canceled resolution does not start a native lookup.</summary>
	[TestMethod]
	public async Task ResolveAsync_RejectsCancellationBeforeLookup()
	{
		var lookupCalls = 0;
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item"),
			_ =>
			{
				lookupCalls++;
				return null;
			});
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await CaptureExceptionAsync<OperationCanceledException>(async () =>
		{
			await source.ResolveAsync(
				new FolderItemKey("win32", Path.Combine(FolderPath, "item")),
				cancellationTokenSource.Token);
		});

		Assert.AreEqual(0, lookupCalls);
	}

	/// <summary>Ensures provider-native resolution failures propagate unchanged.</summary>
	[TestMethod]
	public async Task ResolveAsync_PropagatesNativeFailure()
	{
		var failure = new Win32Exception(5);
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		await using var source = new Win32FolderEnumerationSource(
			FolderPath,
			handle,
			CreateFindData("item"),
			_ => throw failure);

		var exception = await CaptureExceptionAsync<Win32Exception>(async () =>
		{
			await source.ResolveAsync(new FolderItemKey("win32", Path.Combine(FolderPath, "item")));
		});

		Assert.AreSame(failure, exception);
	}

	/// <summary>Ensures resolution cannot start after source disposal.</summary>
	[TestMethod]
	public async Task ResolveAsync_RejectsUseAfterDispose()
	{
		var handle = new ScriptedWin32FindHandle(Array.Empty<Win32PInvoke.WIN32_FIND_DATA>());
		var source = new Win32FolderEnumerationSource(FolderPath, handle, CreateFindData("item"));
		await source.DisposeAsync();

		var exception = await CaptureExceptionAsync<ObjectDisposedException>(async () =>
		{
			await source.ResolveAsync(new FolderItemKey("win32", Path.Combine(FolderPath, "item")));
		});

		Assert.AreEqual(typeof(Win32FolderEnumerationSource).FullName, exception.ObjectName);
	}

	private static Win32PInvoke.WIN32_FIND_DATA CreateFindData(string name, bool isDirectory = false)
		=> new()
		{
			cFileName = name,
			dwFileAttributes = isDirectory ? (uint)FileAttributes.Directory : 0u,
		};

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
}
