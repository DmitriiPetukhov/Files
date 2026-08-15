// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Items;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Navigation;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Navigation;

/// <summary>Verifies Win32 navigation orchestration and scope ownership.</summary>
[TestClass]
public sealed class Win32NavigationExecutorTests
{
	/// <summary>Ensures opened navigation completes through the injected publication coordinator.</summary>
	[TestMethod]
	public async Task ExecuteAsync_ReturnsCompletedAndDisposesOpenedScope()
	{
		var source = new TrackingFolderEnumerationSource();
		var scope = new NavigationScope(source);
		var scopeFactory = new ScriptedNavigationScopeFactory(
			new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Opened,
				scope,
				null,
				null));
		var executor = new Win32NavigationExecutor(
			scopeFactory,
			new ScriptedWin32GitStateResolver(_ => false));
		var coordinator = new RecordingFolderPublicationCoordinator<ListedItem>();
		var initialized = false;

		var result = await executor.ExecuteAsync(
			FolderPath,
			coordinator,
			_ => initialized = true,
			CancellationToken.None);

		Assert.AreEqual(Win32NavigationExecutionStatus.Completed, result.Status);
		Assert.IsTrue(initialized);
		Assert.IsTrue(coordinator.EnumerateCalled);
		Assert.AreEqual(1, source.DisposeCount);
	}

	/// <summary>Ensures a scope is disposed when setup fails after opening it.</summary>
	[TestMethod]
	public async Task ExecuteAsync_DisposesOpenedScopeWhenGitSetupFails()
	{
		var source = new TrackingFolderEnumerationSource();
		var scope = new NavigationScope(source);
		var scopeFactory = new ScriptedNavigationScopeFactory(
			new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Opened,
				scope,
				null,
				null));
		var gitFailure = new InvalidOperationException("git setup failed");
		var gitStateResolver = new ScriptedWin32GitStateResolver(_ => throw gitFailure);
		var executor = new Win32NavigationExecutor(scopeFactory, gitStateResolver);
		var coordinator = new RecordingFolderPublicationCoordinator<ListedItem>();

		var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
			executor.ExecuteAsync(
				FolderPath,
				coordinator,
				_ => { },
				CancellationToken.None));

		Assert.AreSame(gitFailure, exception);
		Assert.AreEqual(1, source.DisposeCount);
		Assert.IsFalse(coordinator.EnumerateCalled);
	}

	/// <summary>Ensures cancellation is reported and the opened scope is still released.</summary>
	[TestMethod]
	public async Task ExecuteAsync_ReturnsCanceledAndDisposesOpenedScope()
	{
		var source = new TrackingFolderEnumerationSource();
		var scope = new NavigationScope(source);
		var scopeFactory = new ScriptedNavigationScopeFactory(
			new NavigationScopeOpenResult(
				NavigationScopeOpenStatus.Opened,
				scope,
				null,
				null));
		var gitStateResolver = new ScriptedWin32GitStateResolver(_ => false);
		using var cancellationTokenSource = new CancellationTokenSource();
		var coordinator = new RecordingFolderPublicationCoordinator<ListedItem>(
			(_, _) =>
			{
				cancellationTokenSource.Cancel();
				throw new OperationCanceledException(cancellationTokenSource.Token);
			});
		var executor = new Win32NavigationExecutor(scopeFactory, gitStateResolver);

		var result = await executor.ExecuteAsync(
			FolderPath,
			coordinator,
			_ => { },
			cancellationTokenSource.Token);

		Assert.AreEqual(Win32NavigationExecutionStatus.Canceled, result.Status);
		Assert.AreEqual(1, source.DisposeCount);
		Assert.IsTrue(coordinator.EnumerateCalled);
	}

	private static string FolderPath => Path.GetTempPath();
}
