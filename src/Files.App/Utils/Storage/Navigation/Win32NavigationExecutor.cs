// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Data.Items;
using Files.App.Helpers;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators.Win32;
using Files.App.Utils.Storage.Projections;
using Windows.Win32.Foundation;

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Owns Win32 scope lifetime while coordinating Git setup and publication.</summary>
internal sealed class Win32NavigationExecutor : IWin32NavigationExecutor
{
	private readonly INavigationScopeFactory scopeFactory;
	private readonly IWin32GitStateResolver gitStateResolver;

	/// <summary>Creates a Win32 navigation executor from its provider dependencies.</summary>
	/// <param name="scopeFactory">Factory that owns provider scope creation.</param>
	/// <param name="gitStateResolver">Resolver for legacy Git-aware materialization.</param>
	public Win32NavigationExecutor(
		INavigationScopeFactory scopeFactory,
		IWin32GitStateResolver gitStateResolver)
	{
		this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
		this.gitStateResolver = gitStateResolver ?? throw new ArgumentNullException(nameof(gitStateResolver));
	}

	/// <inheritdoc />
	public async Task<Win32NavigationExecutionResult> ExecuteAsync(
		string path,
		IFolderPublicationCoordinator<ListedItem> publicationCoordinator,
		Action<FolderItemMetadata?> initializeCurrentFolder,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(publicationCoordinator);
		ArgumentNullException.ThrowIfNull(initializeCurrentFolder);

		using var openTimeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using var openCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			openTimeoutCancellation.Token);
		var openResult = await scopeFactory.TryCreateAsync(
			new FolderReference("win32", path),
			openCancellation.Token);

		if (openResult.Status != NavigationScopeOpenStatus.Opened)
		{
			initializeCurrentFolder(openResult.InitialMetadata);
			return MapOpenResult(
				openResult,
				openTimeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
		}

		var navigationScope = openResult.Scope ??
			throw new InvalidOperationException("An opened Win32 navigation result must own a scope.");
		await using var ownedNavigationScope = navigationScope;
		initializeCurrentFolder(openResult.InitialMetadata);

		try
		{
			await Task.Run(async () =>
			{
				var isGitRepo = await gitStateResolver.IsRepositoryAsync(path, cancellationToken);
				IFolderEnumerationSource<ListedItem> source = new Win32ListedItemEnumerationAdapter(
					ownedNavigationScope.EnumerationSource,
					new FolderItemListedItemProjection(),
					path,
					isGitRepo);

				await publicationCoordinator.EnumerateAsync(source, cancellationToken);
			},
			cancellationToken);

			return new Win32NavigationExecutionResult(Win32NavigationExecutionStatus.Completed);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return new Win32NavigationExecutionResult(Win32NavigationExecutionStatus.Canceled);
		}
		catch (Win32Exception ex)
		{
			return new Win32NavigationExecutionResult(
				Win32NavigationExecutionStatus.Failed,
				MapFailureReason(ex.NativeErrorCode),
				ex.NativeErrorCode.ToString());
		}
	}

	private static Win32NavigationExecutionResult MapOpenResult(
		NavigationScopeOpenResult openResult,
		bool openTimedOut)
		=> openResult.Status switch
		{
			NavigationScopeOpenStatus.Fallback => new Win32NavigationExecutionResult(
				Win32NavigationExecutionStatus.Fallback,
				openResult.FailureReason),
			NavigationScopeOpenStatus.Unavailable => new Win32NavigationExecutionResult(
				Win32NavigationExecutionStatus.Unavailable,
				openResult.FailureReason),
			NavigationScopeOpenStatus.Canceled => new Win32NavigationExecutionResult(
				Win32NavigationExecutionStatus.Canceled,
				OpenTimedOut: openTimedOut),
			_ => throw new InvalidOperationException("The Win32 scope-open result is invalid."),
		};

	private static NavigationUnavailableReason MapFailureReason(int nativeErrorCode)
		=> (WIN32_ERROR)nativeErrorCode == WIN32_ERROR.ERROR_ACCESS_DENIED
			? NavigationUnavailableReason.AccessDenied
			: NavigationUnavailableReason.DriveUnplugged;
}
