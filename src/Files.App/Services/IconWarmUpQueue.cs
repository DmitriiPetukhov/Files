// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Utils;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Files.App.Services
{
	internal sealed class IconWarmUpQueue : IAsyncDisposable
	{
		private const int DefaultCapacity = 256;
		private const int DefaultWorkerCount = 4;

		private readonly Channel<WarmUpRequest> queue;
		private readonly IIconCacheService iconCacheService;
		private readonly ILogger<IconWarmUpQueue> logger;
		private readonly int capacity;
		private readonly Task[] workers;
		private long acceptedCount;
		private long completedCount;
		private long droppedCount;
		private long failureCount;
		private long staleSkippedCount;
		private int disposed;

		internal IconWarmUpQueue(IIconCacheService iconCacheService, ILogger<IconWarmUpQueue> logger)
			: this(iconCacheService, logger, DefaultCapacity, DefaultWorkerCount)
		{
		}

		internal IconWarmUpQueue(IIconCacheService iconCacheService, ILogger<IconWarmUpQueue> logger, int capacity, int workerCount)
		{
			ArgumentNullException.ThrowIfNull(iconCacheService);
			ArgumentNullException.ThrowIfNull(logger);

			if (capacity <= 0)
				throw new ArgumentOutOfRangeException(nameof(capacity));

			if (workerCount <= 0)
				throw new ArgumentOutOfRangeException(nameof(workerCount));

			this.iconCacheService = iconCacheService;
			this.logger = logger;
			this.capacity = capacity;
			queue = Channel.CreateBounded<WarmUpRequest>(new BoundedChannelOptions(capacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = false,
				SingleWriter = false,
			});

			workers = Enumerable.Range(0, workerCount)
				.Select(_ => Task.Run(ConsumeAsync))
				.ToArray();
		}

		internal long AcceptedCount => Interlocked.Read(ref acceptedCount);
		internal long CompletedCount => Interlocked.Read(ref completedCount);
		internal long DroppedCount => Interlocked.Read(ref droppedCount);
		internal long FailureCount => Interlocked.Read(ref failureCount);
		internal long StaleSkippedCount => Interlocked.Read(ref staleSkippedCount);

		internal bool TryQueue(ListedItem item, bool isFolderFromEnumeration, CancellationToken navigationToken)
		{
			ArgumentNullException.ThrowIfNull(item);

			if (Volatile.Read(ref disposed) != 0 || navigationToken.IsCancellationRequested)
			{
				Interlocked.Increment(ref staleSkippedCount);
				return false;
			}

			if (queue.Writer.TryWrite(new WarmUpRequest(item, isFolderFromEnumeration, navigationToken)))
			{
				Interlocked.Increment(ref acceptedCount);
				return true;
			}

			var dropped = Interlocked.Increment(ref droppedCount);
			if (dropped % 256 == 0)
				logger.LogDebug("Icon warm-up queue dropped {Count} optional requests at capacity {Capacity}", dropped, capacity);

			return false;
		}

		private async Task ConsumeAsync()
		{
			try
			{
				await foreach (var request in queue.Reader.ReadAllAsync().ConfigureAwait(false))
					await ProcessAsync(request).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Icon warm-up worker stopped unexpectedly");
			}
		}

		private async Task ProcessAsync(WarmUpRequest request)
		{
			if (request.NavigationToken.IsCancellationRequested)
			{
				Interlocked.Increment(ref staleSkippedCount);
				return;
			}

			try
			{
				var icon = await iconCacheService.GetIconAsync(
					request.Item.ItemPath,
					request.Item.FileExtension,
					request.IsFolderFromEnumeration).ConfigureAwait(false);

				if (request.NavigationToken.IsCancellationRequested)
				{
					Interlocked.Increment(ref staleSkippedCount);
					return;
				}

				if (icon is not null)
					request.Item.TrySetPreloadedIconData(icon);

				Interlocked.Increment(ref completedCount);
			}
			catch (OperationCanceledException) when (request.NavigationToken.IsCancellationRequested)
			{
				Interlocked.Increment(ref staleSkippedCount);
			}
			catch (Exception ex)
			{
				Interlocked.Increment(ref failureCount);
				logger.LogDebug(ex, "Icon warm-up failed [{Id}] '{Extension}'", request.Item.ItemPath?.GetHashCode() ?? 0, request.Item.FileExtension ?? ":folder:");
			}
		}

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			queue.Writer.TryComplete();
			await Task.WhenAll(workers).ConfigureAwait(false);
			logger.LogDebug(
				"Icon warm-up queue stopped; accepted={Accepted}, completed={Completed}, dropped={Dropped}, stale={Stale}, failures={Failures}",
				AcceptedCount,
				CompletedCount,
				DroppedCount,
				StaleSkippedCount,
				FailureCount);
		}

		private readonly record struct WarmUpRequest(
			ListedItem Item,
			bool IsFolderFromEnumeration,
			CancellationToken NavigationToken);
	}
}
