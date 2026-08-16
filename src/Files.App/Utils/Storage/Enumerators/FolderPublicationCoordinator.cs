// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using System.Threading.Channels;

namespace Files.App.Utils.Storage;

/// <summary>
/// Coordinates a provider-neutral state stream and its navigation-scoped lifecycle.
/// </summary>
internal sealed class FolderPublicationCoordinator : IFolderPublicationCoordinator, IAsyncDisposable
{
	private const int DefaultStateBufferCapacity = 32;

	private readonly FolderPublicationSession session = new();
	private readonly Channel<FolderPublicationState> stateStream;
	private readonly object lifecycleSyncRoot = new();
	private IFolderPublicationEnrichment? enrichment;
	private readonly SemaphoreSlim stateGate = new(1, 1);
	private readonly CancellationTokenSource lifecycleCancellation = new();
	private Task? enumerationTask;
	private int isStarted;
	private int isCanceled;
	private int isFinalPublished;
	private int isDisposed;

	/// <summary>Creates a state coordinator with an optional bounded enrichment boundary.</summary>
	/// <param name="enrichment">Optional late enrichment owner.</param>
	/// <param name="stateBufferCapacity">Bounded worker-level state capacity.</param>
	public FolderPublicationCoordinator(
		IFolderPublicationEnrichment? enrichment = null,
		int stateBufferCapacity = DefaultStateBufferCapacity)
	{
		if (stateBufferCapacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(stateBufferCapacity));

		this.enrichment = enrichment;
		stateStream = Channel.CreateBounded<FolderPublicationState>(new BoundedChannelOptions(stateBufferCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true,
		});
	}

	/// <inheritdoc />
	public IAsyncEnumerable<FolderPublicationState> ReadStates(
		CancellationToken cancellationToken = default)
		=> stateStream.Reader.ReadAllAsync(cancellationToken);

	/// <inheritdoc />
	public Task EnumerateAsync(
		IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> batches,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batches);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
		if (Interlocked.Exchange(ref isStarted, 1) != 0)
			throw new InvalidOperationException("Folder publication enumeration has already started.");

		var task = EnumerateCoreAsync(batches, cancellationToken);
		Volatile.Write(ref enumerationTask, task);
		return task;
	}

	/// <inheritdoc />
	public bool TrySetEnrichment(IFolderPublicationEnrichment enrichment)
	{
		ArgumentNullException.ThrowIfNull(enrichment);

		lock (lifecycleSyncRoot)
		{
			if (Volatile.Read(ref isStarted) != 0 || Volatile.Read(ref isCanceled) != 0 || this.enrichment is not null)
				return false;

			this.enrichment = enrichment;
			return true;
		}
	}

	/// <inheritdoc />
	public async Task<bool> TryApplyUpdateAsync(
		FolderItemKey key,
		FolderItem item,
		long expectedRevision,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (Volatile.Read(ref isCanceled) != 0)
			return false;

		await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!session.TryApplyUpdate(key, item, expectedRevision, cancellationToken, out var state))
				return false;

			await stateStream.Writer.WriteAsync(state!, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (ChannelClosedException)
		{
			return false;
		}
		finally
		{
			stateGate.Release();
		}
	}

	/// <inheritdoc />
	public async Task CancelAsync()
	{
		await TerminateAsync(null).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) != 0)
			return;

		await TerminateAsync(null).ConfigureAwait(false);
		if (Volatile.Read(ref enumerationTask) is { } task)
		{
			try
			{
				await task.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception)
			{
			}
		}

		if (enrichment is not null)
			await enrichment.DisposeAsync().ConfigureAwait(false);

		lifecycleCancellation.Dispose();
		stateGate.Dispose();
	}

	private async Task EnumerateCoreAsync(
		IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> batches,
		CancellationToken cancellationToken)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			lifecycleCancellation.Token);

		try
		{
			await foreach (var batch in batches.WithCancellation(linkedCancellation.Token))
				await PublishBatchAsync(batch, linkedCancellation.Token).ConfigureAwait(false);

			if (enrichment is not null)
				await enrichment.CompleteAsync(linkedCancellation.Token).ConfigureAwait(false);

			await PublishFinalAsync(linkedCancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
		{
			await TerminateAsync(null).ConfigureAwait(false);
			throw;
		}
		catch (Exception exception)
		{
			await TerminateAsync(exception).ConfigureAwait(false);
			throw;
		}
	}

	private async Task PublishBatchAsync(
		FolderEnumerationBatch<FolderItem> batch,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batch);
		List<(FolderItem Item, long Revision)>? enrichmentItems = null;

		await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (Volatile.Read(ref isCanceled) != 0 ||
				!session.TryAppend(batch, cancellationToken, out var state))
				return;

			await stateStream.Writer.WriteAsync(state!, cancellationToken).ConfigureAwait(false);
			if (enrichment is not null)
			{
				enrichmentItems = new List<(FolderItem Item, long Revision)>(batch.Items.Count);
				foreach (var item in batch.Items)
				{
					if (session.TryGetRevision(item.Key, out var revision))
						enrichmentItems.Add((item, revision));
				}
			}
		}
		finally
		{
			stateGate.Release();
		}

		if (enrichment is null || enrichmentItems is null)
			return;

		foreach (var (item, revision) in enrichmentItems)
			await enrichment.EnqueueAsync(item, revision, cancellationToken).ConfigureAwait(false);
	}

	private async Task PublishFinalAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref isFinalPublished, 1) != 0)
			return;

		await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (Volatile.Read(ref isCanceled) != 0)
				return;

			session.Complete();
			var finalState = session.GetCurrentState() with { IsFinal = true };
			await stateStream.Writer.WriteAsync(finalState, cancellationToken).ConfigureAwait(false);
			stateStream.Writer.TryComplete();
		}
		finally
		{
			stateGate.Release();
		}
	}

	private async Task TerminateAsync(Exception? completionError)
	{
		if (Interlocked.Exchange(ref isCanceled, 1) == 0)
		{
			lifecycleCancellation.Cancel();
			session.Cancel();

			if (enrichment is not null)
				await enrichment.CancelAsync().ConfigureAwait(false);
		}

		stateStream.Writer.TryComplete(completionError);
	}
}

/// <summary>
/// Coordinates one source enumeration with its canonical publication session.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal sealed class FolderPublicationCoordinator<T> : IFolderPublicationCoordinator<T>
{
	private readonly IFolderPublicationSession<T> session;
	private readonly FolderPublicationOperationGate operationGate = new();
	private readonly Func<IReadOnlyCollection<T>, Task> publishSnapshotAsync;
	private int isActive = 1;

	public FolderPublicationCoordinator(
		IComparer<T> itemComparer,
		Func<IReadOnlyCollection<T>, Task> publishSnapshotAsync)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);
		ArgumentNullException.ThrowIfNull(publishSnapshotAsync);

		session = new FolderPublicationSession<T>(itemComparer);
		this.publishSnapshotAsync = publishSnapshotAsync;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);

		var finalItems = await source.EnumerateAsync(
			batch => PublishBatchAsync(batch, cancellationToken),
			cancellationToken);

		await PublishFinalAsync(finalItems, cancellationToken);

		return finalItems;
	}

	/// <inheritdoc />
	public Task<bool> TryRebuildIndexAsync(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);

		return operationGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var rebuildResult = await Task.Run(() =>
			{
				var accepted = session.TryRebuildIndex(itemComparer, cancellationToken, out var snapshot);
				return (Accepted: accepted, Snapshot: snapshot);
			});

			if (!rebuildResult.Accepted || !CanPublish(cancellationToken))
				return false;

			await publishSnapshotAsync(rebuildResult.Snapshot!);
			return true;
		});
	}

	/// <inheritdoc />
	public Task CancelAsync()
	{
		return operationGate.ExecuteAsync(() =>
		{
			Interlocked.Exchange(ref isActive, 0);
			session.Cancel();

			return Task.FromResult(true);
		});
	}

	private Task PublishBatchAsync(
		IReadOnlyCollection<T> batch,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batch);

		return operationGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var accepted = session.TryAppend(batch, cancellationToken, out var snapshot);
			if (!accepted)
				return false;

			await publishSnapshotAsync(snapshot!);
			return true;
		});
	}

	private Task PublishFinalAsync(
		IReadOnlyCollection<T> items,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(items);

		return operationGate.ExecuteAsync(async () =>
		{
			if (!CanPublish(cancellationToken))
				return false;

			var accepted = session.TryReplaceFinal(items, cancellationToken, out var snapshot);
			if (!accepted)
				return false;

			await publishSnapshotAsync(snapshot!);
			return true;
		});
	}

	private bool CanPublish(CancellationToken cancellationToken)
		=> Volatile.Read(ref isActive) != 0 && !cancellationToken.IsCancellationRequested;
}
