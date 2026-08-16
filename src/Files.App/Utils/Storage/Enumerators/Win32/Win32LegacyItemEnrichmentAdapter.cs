// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Threading.Channels;
using Files.App.Data.Contracts;
using Files.App.Services;
using Files.App.Services.SizeProvider;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Projections;
using Microsoft.Extensions.Logging;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Runs bounded optional Win32 legacy materialization after cheap state acceptance.</summary>
internal sealed class Win32LegacyItemEnrichmentAdapter : IFolderPublicationEnrichment, IAsyncDisposable
{
	private const int DefaultQueueCapacity = 64;
	private const int DefaultWorkerCount = 4;

	private readonly FolderItemListedItemProjection projection;
	private readonly Func<FolderItem, CancellationToken, Task<Win32LegacyItemEnrichmentResult?>> materialize;
	private readonly Func<FolderItemKey, FolderItem, long, CancellationToken, Task<bool>> applyUpdateAsync;
	private readonly Channel<EnrichmentWorkItem> queue;
	private readonly CancellationTokenSource workerCancellation = new();
	private readonly Task[] workers;
	private readonly object lifecycleSyncRoot = new();
	private Task? workersCompletionTask;
	private int isCompleting;
	private int isDisposed;

	/// <summary>Creates a testable bounded enrichment adapter over a caller-owned materializer.</summary>
	/// <param name="gitStateTask">Navigation-scoped Git state task.</param>
	/// <param name="projection">Navigation-scoped compatibility overlay.</param>
	/// <param name="materialize">Legacy materialization operation.</param>
	/// <param name="applyUpdateAsync">Revision-checked state update callback.</param>
	/// <param name="queueCapacity">Bounded queued-work capacity.</param>
	/// <param name="workerCount">Number of enrichment workers.</param>
	internal Win32LegacyItemEnrichmentAdapter(
		Task<bool> gitStateTask,
		FolderItemListedItemProjection projection,
		Func<FolderItem, CancellationToken, Task<Win32LegacyItemEnrichmentResult?>> materialize,
		Func<FolderItemKey, FolderItem, long, CancellationToken, Task<bool>> applyUpdateAsync,
		int queueCapacity = DefaultQueueCapacity,
		int workerCount = DefaultWorkerCount)
	{
		ArgumentNullException.ThrowIfNull(gitStateTask);
		this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
		this.materialize = materialize ?? throw new ArgumentNullException(nameof(materialize));
		this.applyUpdateAsync = applyUpdateAsync ?? throw new ArgumentNullException(nameof(applyUpdateAsync));
		if (queueCapacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(queueCapacity));
		if (workerCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(workerCount));

		queue = Channel.CreateBounded<EnrichmentWorkItem>(new BoundedChannelOptions(queueCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = false,
			SingleWriter = true,
		});
		workers = Enumerable.Range(0, workerCount)
			.Select(_ => Task.Run(ConsumeAsync))
			.ToArray();
	}

	/// <summary>Creates the production Win32 materializer while retaining the same bounded worker boundary.</summary>
	/// <param name="legacyRootPath">Root path used by legacy item materialization.</param>
	/// <param name="gitStateTask">Navigation-scoped Git state task.</param>
	/// <param name="projection">Navigation-scoped compatibility overlay.</param>
	/// <param name="userSettingsService">Settings used for ADS and folder-size compatibility.</param>
	/// <param name="iconWarmUpQueue">Existing asynchronous icon warm-up service.</param>
	/// <param name="folderSizeProvider">Existing folder-size service.</param>
	/// <param name="applyUpdateAsync">Revision-checked state update callback.</param>
	internal Win32LegacyItemEnrichmentAdapter(
		string legacyRootPath,
		Task<bool> gitStateTask,
		FolderItemListedItemProjection projection,
		IUserSettingsService userSettingsService,
		IconWarmUpQueue iconWarmUpQueue,
		ISizeProvider folderSizeProvider,
		Func<FolderItemKey, FolderItem, long, CancellationToken, Task<bool>> applyUpdateAsync)
		: this(
			gitStateTask,
			projection,
			(item, cancellationToken) => MaterializeLegacyItemAsync(
				item,
				legacyRootPath,
				gitStateTask,
				projection,
				userSettingsService,
				iconWarmUpQueue,
				folderSizeProvider,
				cancellationToken),
			applyUpdateAsync)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(legacyRootPath);
		ArgumentNullException.ThrowIfNull(userSettingsService);
		ArgumentNullException.ThrowIfNull(iconWarmUpQueue);
		ArgumentNullException.ThrowIfNull(folderSizeProvider);
	}

	/// <inheritdoc />
	public async ValueTask EnqueueAsync(
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (Volatile.Read(ref isCompleting) != 0 || Volatile.Read(ref isDisposed) != 0)
			return;

		try
		{
			await queue.Writer.WriteAsync(
				new EnrichmentWorkItem(item, expectedRevision, cancellationToken),
				cancellationToken).ConfigureAwait(false);
		}
		catch (ChannelClosedException)
		{
		}
	}

	/// <inheritdoc />
	public Task CompleteAsync(CancellationToken cancellationToken)
		=> AwaitWorkersAsync(cancelWorkers: false, cancellationToken);

	/// <inheritdoc />
	public Task CancelAsync()
		=> AwaitWorkersAsync(cancelWorkers: true, CancellationToken.None);

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
			return;

		await CancelAsync().ConfigureAwait(false);
		workerCancellation.Dispose();
	}

	private async Task ConsumeAsync()
	{
		try
		{
			await foreach (var workItem in queue.Reader.ReadAllAsync(workerCancellation.Token).ConfigureAwait(false))
				await ProcessAsync(workItem).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (workerCancellation.IsCancellationRequested)
		{
		}
	}

	private async Task ProcessAsync(EnrichmentWorkItem workItem)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			workerCancellation.Token,
			workItem.NavigationToken);
		Win32LegacyItemEnrichmentResult? result = null;
		try
		{
			result = await materialize(workItem.Item, linkedCancellation.Token).ConfigureAwait(false);
			if (result is null || linkedCancellation.IsCancellationRequested)
				return;

			projection.ApplyLegacyOverlay(
				workItem.Item.Key,
				result.PrimaryItem,
				result.AdditionalItems,
				workItem.ExpectedRevision);
			if (!await applyUpdateAsync(
				workItem.Item.Key,
				workItem.Item,
				workItem.ExpectedRevision,
				linkedCancellation.Token).ConfigureAwait(false))
			{
				projection.RemoveLegacyOverlay(workItem.Item.Key, result.PrimaryItem, workItem.ExpectedRevision);
			}
		}
		catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			projection.RemoveLegacyOverlay(workItem.Item.Key, result?.PrimaryItem, workItem.ExpectedRevision);
			App.Logger.LogWarning(
				exception,
				"Win32 legacy enrichment failed. Path={Path} ErrorType={ErrorType} NativeErrorCode={NativeErrorCode}",
				LogPathHelper.GetPathIdentifier(workItem.Item.Key.OpaqueId),
				exception.GetType().Name,
				(exception as Win32Exception)?.NativeErrorCode);
		}
	}

	private async Task AwaitWorkersAsync(bool cancelWorkers, CancellationToken cancellationToken)
	{
		Task completionTask;
		lock (lifecycleSyncRoot)
		{
			Interlocked.Exchange(ref isCompleting, 1);
			if (cancelWorkers)
				workerCancellation.Cancel();

			queue.Writer.TryComplete();
			workersCompletionTask ??= Task.WhenAll(workers);
			completionTask = workersCompletionTask;
		}

		await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	private static async Task<Win32LegacyItemEnrichmentResult?> MaterializeLegacyItemAsync(
		FolderItem item,
		string legacyRootPath,
		Task<bool> gitStateTask,
		FolderItemListedItemProjection projection,
		IUserSettingsService userSettingsService,
		IconWarmUpQueue iconWarmUpQueue,
		ISizeProvider folderSizeProvider,
		CancellationToken cancellationToken)
	{
		if (item.ProviderData is not Win32FolderItemData providerData)
			return new Win32LegacyItemEnrichmentResult(projection.Project(item), Array.Empty<ListedItem>());

		var isGitRepo = await gitStateTask.WaitAsync(cancellationToken).ConfigureAwait(false);
		var findData = providerData.FindData;
		var fileAttributes = (FileAttributes)findData.dwFileAttributes;
		var isFolder = fileAttributes.HasFlag(FileAttributes.Directory);
		var listedItem = isFolder
			? await Win32StorageEnumerator.GetFolder(findData, legacyRootPath, isGitRepo, cancellationToken).ConfigureAwait(false)
			: await Win32StorageEnumerator.GetFile(findData, legacyRootPath, isGitRepo, cancellationToken).ConfigureAwait(false);
		if (listedItem is null)
			return null;

		iconWarmUpQueue.TryQueue(listedItem, isFolder, cancellationToken);
		var additionalItems = new List<ListedItem>();
		var foldersSettings = userSettingsService.FoldersSettingsService;
		if (foldersSettings.AreAlternateStreamsVisible)
		{
			foreach (var stream in Win32Helper.GetAlternateStreams(listedItem.ItemPath))
				additionalItems.Add(Win32StorageEnumerator.GetAlternateStream(stream, listedItem));
		}

		if (isFolder && foldersSettings.CalculateFolderSizes)
		{
			if (folderSizeProvider.TryGetSize(listedItem.ItemPath, out var size))
			{
				listedItem.FileSizeBytes = (long)size;
				listedItem.FileSize = size.ToSizeString();
			}

			_ = folderSizeProvider.UpdateAsync(listedItem.ItemPath, cancellationToken);
		}

		return new Win32LegacyItemEnrichmentResult(listedItem, additionalItems);
	}

	private readonly record struct EnrichmentWorkItem(
		FolderItem Item,
		long ExpectedRevision,
		CancellationToken NavigationToken);
}
