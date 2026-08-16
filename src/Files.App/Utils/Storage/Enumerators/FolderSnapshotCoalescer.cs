// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>Applies worker snapshots with one in-flight operation and latest-wins pending state.</summary>
/// <typeparam name="T">Item type in each projected snapshot.</typeparam>
internal sealed class FolderSnapshotCoalescer<T> : IFolderSnapshotCoalescer<T>, IAsyncDisposable
{
	private readonly object syncRoot = new();
	private readonly IFolderSnapshotScheduler scheduler;
	private readonly Func<IReadOnlyCollection<T>, Task> applySnapshotAsync;
	private readonly Func<bool> isCurrent;
	private Task? inFlightTask;
	private PendingSnapshot? pendingSnapshot;
	private PendingSnapshot? failedFinalSnapshot;
	private TaskCompletionSource<bool>? drainCompletion;
	private bool finalSubmitted;
	private bool isCanceled;

	/// <summary>Creates a coalescer over a caller-owned scheduler and UI apply callback.</summary>
	/// <param name="scheduler">Boundary that schedules one callback at a time.</param>
	/// <param name="applySnapshotAsync">Callback that applies a snapshot at the UI boundary.</param>
	/// <param name="isCurrent">Generation predicate checked before UI application.</param>
	public FolderSnapshotCoalescer(
		IFolderSnapshotScheduler scheduler,
		Func<IReadOnlyCollection<T>, Task> applySnapshotAsync,
		Func<bool>? isCurrent = null)
	{
		this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
		this.applySnapshotAsync = applySnapshotAsync ?? throw new ArgumentNullException(nameof(applySnapshotAsync));
		this.isCurrent = isCurrent ?? (() => true);
	}

	/// <inheritdoc />
	public void Submit(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		SubmitCore(new PendingSnapshot(CopySnapshot(snapshot), IsFinal: false, cancellationToken));
	}

	/// <inheritdoc />
	public void SubmitFinal(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		SubmitCore(new PendingSnapshot(CopySnapshot(snapshot), IsFinal: true, cancellationToken));
	}

	/// <inheritdoc />
	public async Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false)
	{
		TaskCompletionSource<bool>? pumpCompletion = null;
		Task waitTask;
		lock (syncRoot)
		{
			if (isCanceled)
				return;

			if (retryPendingSnapshot && failedFinalSnapshot is { } failedFinal)
			{
				pendingSnapshot = failedFinal;
				failedFinalSnapshot = null;
			}

			if (pendingSnapshot is null && inFlightTask is null)
				return;

			drainCompletion ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			waitTask = drainCompletion.Task;
			pumpCompletion = StartPumpLocked();
		}

		StartPump(pumpCompletion);
		await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task CancelAsync()
	{
		Task? pumpTask;
		lock (syncRoot)
		{
			isCanceled = true;
			pendingSnapshot = null;
			failedFinalSnapshot = null;
			drainCompletion?.TrySetResult(true);
			drainCompletion = null;
			pumpTask = inFlightTask;
		}

		if (pumpTask is not null)
			await pumpTask.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
		=> await CancelAsync().ConfigureAwait(false);

	private void SubmitCore(PendingSnapshot snapshot)
	{
		TaskCompletionSource<bool>? pumpCompletion = null;
		lock (syncRoot)
		{
			if (isCanceled || snapshot.CancellationToken.IsCancellationRequested || !isCurrent() ||
				(!snapshot.IsFinal && finalSubmitted))
				return;

			if (snapshot.IsFinal)
				finalSubmitted = true;

			pendingSnapshot = snapshot;
			pumpCompletion = StartPumpLocked();
		}

		StartPump(pumpCompletion);
	}

	private TaskCompletionSource<bool>? StartPumpLocked()
	{
		if (inFlightTask is not null || pendingSnapshot is null || isCanceled)
			return null;

		var pumpCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		inFlightTask = pumpCompletion.Task;
		return pumpCompletion;
	}

	private void StartPump(TaskCompletionSource<bool>? pumpCompletion)
	{
		if (pumpCompletion is not null)
			_ = RunPumpAsync(pumpCompletion);
	}

	private async Task RunPumpAsync(TaskCompletionSource<bool> pumpCompletion)
	{
		try
		{
			while (true)
			{
				PendingSnapshot snapshot;
				lock (syncRoot)
				{
					if (isCanceled || pendingSnapshot is null)
					{
						if (ReferenceEquals(inFlightTask, pumpCompletion.Task))
							inFlightTask = null;
						drainCompletion?.TrySetResult(true);
						drainCompletion = null;
						break;
					}

					snapshot = pendingSnapshot!;
					pendingSnapshot = null;
				}

				try
				{
					await scheduler.ScheduleAsync(() => ApplySnapshotAsync(snapshot)).ConfigureAwait(false);
				}
				catch
				{
					if (snapshot.IsFinal)
					{
						lock (syncRoot)
							failedFinalSnapshot = snapshot;
					}
				}
			}
		}
		finally
		{
			pumpCompletion.TrySetResult(true);
		}
	}

	private async Task ApplySnapshotAsync(PendingSnapshot snapshot)
	{
		if (snapshot.CancellationToken.IsCancellationRequested || Volatile.Read(ref isCanceled) || !isCurrent())
			return;

		await applySnapshotAsync(snapshot.Snapshot).ConfigureAwait(false);
	}

	private static IReadOnlyCollection<T> CopySnapshot(IReadOnlyCollection<T> snapshot)
		=> Array.AsReadOnly(snapshot.ToArray());

	private sealed record PendingSnapshot(
		IReadOnlyCollection<T> Snapshot,
		bool IsFinal,
		CancellationToken CancellationToken);
}
