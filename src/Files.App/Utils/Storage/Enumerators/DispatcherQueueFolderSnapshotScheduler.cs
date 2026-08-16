// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Dispatching;

namespace Files.App.Utils.Storage;

/// <summary>Bridges one coalesced snapshot callback to the existing dispatcher boundary.</summary>
internal sealed class DispatcherQueueFolderSnapshotScheduler : IFolderSnapshotScheduler
{
	private readonly DispatcherQueue dispatcherQueue;

	/// <summary>Creates a scheduler for the supplied UI dispatcher.</summary>
	/// <param name="dispatcherQueue">Dispatcher used by the existing UI boundary.</param>
	public DispatcherQueueFolderSnapshotScheduler(DispatcherQueue dispatcherQueue)
		=> this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

	/// <inheritdoc />
	public Task ScheduleAsync(Func<Task> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return dispatcherQueue.EnqueueOrInvokeAsync(callback);
	}
}
