// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Files.App.Utils.Storage;

internal sealed class Win32FolderChangeQueueAdapter
{
	private readonly ConcurrentQueue<(uint Action, string FileName)> operationQueue;
	private readonly FolderChangeQueueGate operationQueueGate;
	private readonly Action signalOperationEvent;

	public Win32FolderChangeQueueAdapter(
		ConcurrentQueue<(uint Action, string FileName)> operationQueue,
		FolderChangeQueueGate operationQueueGate,
		Action signalOperationEvent)
	{
		this.operationQueue = operationQueue;
		this.operationQueueGate = operationQueueGate;
		this.signalOperationEvent = signalOperationEvent;
	}

	public bool Publish(
		int watcherGeneration,
		CancellationToken cancellationToken,
		IReadOnlyCollection<Win32FolderChangeNotification> notifications)
	{
		ArgumentNullException.ThrowIfNull(notifications);

		return operationQueueGate.TryRun(watcherGeneration, cancellationToken, () =>
		{
			foreach (var notification in notifications)
			{
				if (notification.Action != Win32FolderChangeAction.Unknown)
					operationQueue.Enqueue(((uint)notification.Action, notification.FullPath));
			}

			if (notifications.Count > 0)
				signalOperationEvent();
		});
	}
}
