using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.UnitTests.TestHelpers;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Navigation;

/// <summary>Verifies incremental navigation graph composition.</summary>
[TestClass]
public sealed class NavigationScopeFactoryTests
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

	/// <summary>Ensures the universal factory result exposes an opened navigation scope.</summary>
	[TestMethod]
	public async Task TryCreateAsync_ReturnsOpenedScopeForWin32Folder()
	{
		File.WriteAllText(Path.Combine(FolderPath, "item.txt"), "item");
		var factory = new NavigationScopeFactory();

		var result = await factory.TryCreateAsync(
			new FolderReference("win32", FolderPath),
			CancellationToken.None);

		Assert.AreEqual(NavigationScopeOpenStatus.Opened, result.Status);
		Assert.IsNotNull(result.Scope);
		Assert.IsNotNull(result.InitialMetadata);

		await using var scope = result.Scope;
		var batches = new List<FolderEnumerationBatch<FolderItem>>();
		await foreach (var batch in scope.EnumerationSource.EnumerateAsync())
			batches.Add(batch);

		Assert.AreEqual(typeof(Win32FolderEnumerationSource), scope.EnumerationSource.GetType());
		Assert.IsTrue(batches.Count > 0);
	}

	/// <summary>Ensures unsupported providers are rejected before scope creation.</summary>
	[TestMethod]
	public async Task TryCreateAsync_RejectsUnsupportedProvider()
	{
		var factory = new NavigationScopeFactory();

		var exception = await CaptureExceptionAsync<ArgumentException>(() =>
			factory.TryCreateAsync(
				new FolderReference("ftp", FolderPath),
				CancellationToken.None));

		Assert.AreEqual("folder", exception.ParamName);
	}

	/// <summary>Ensures a missing folder reference is rejected before scope creation.</summary>
	[TestMethod]
	public async Task TryCreateAsync_RejectsMissingFolder()
	{
		var factory = new NavigationScopeFactory();

		var exception = await CaptureExceptionAsync<ArgumentNullException>(() =>
			factory.TryCreateAsync(null!, CancellationToken.None));

		Assert.AreEqual("folder", exception.ParamName);
	}

	/// <summary>Ensures a missing provider folder requests the universal fallback outcome.</summary>
	[TestMethod]
	public async Task TryCreateAsync_ReturnsFallbackForMissingWin32Folder()
	{
		var factory = new NavigationScopeFactory();

		var result = await factory.TryCreateAsync(
			new FolderReference("win32", Path.Combine(FolderPath, "missing")),
			CancellationToken.None);

		Assert.AreEqual(NavigationScopeOpenStatus.Fallback, result.Status);
		Assert.IsNull(result.Scope);
		Assert.AreEqual(NavigationUnavailableReason.NotFound, result.FailureReason);
	}

	/// <summary>Ensures cancellation produces a universal silent-cancellation outcome.</summary>
	[TestMethod]
	public async Task TryCreateAsync_ReturnsCanceledWhenCanceledBeforeOpen()
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		var factory = new NavigationScopeFactory();

		var result = await factory.TryCreateAsync(
			new FolderReference("win32", FolderPath),
			cancellationTokenSource.Token);

		Assert.AreEqual(NavigationScopeOpenStatus.Canceled, result.Status);
		Assert.IsNull(result.Scope);
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
}
