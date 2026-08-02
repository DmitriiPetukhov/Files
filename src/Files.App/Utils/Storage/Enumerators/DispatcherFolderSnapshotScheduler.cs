// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;

namespace Files.App.Utils.Storage;

internal sealed class DispatcherFolderSnapshotScheduler : IFolderSnapshotScheduler
{
	private readonly DispatcherQueue? dispatcherQueue;
	private readonly DispatcherQueuePriority priority;

	public DispatcherFolderSnapshotScheduler(DispatcherQueue? dispatcherQueue, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
	{
		this.dispatcherQueue = dispatcherQueue;
		this.priority = priority;
	}

	public Task ScheduleAsync(Func<Task> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
			return callback();

		return dispatcherQueue.EnqueueAsync(callback, priority);
	}
}
