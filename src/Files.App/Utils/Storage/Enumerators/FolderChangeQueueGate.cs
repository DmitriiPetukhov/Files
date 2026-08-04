// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class FolderChangeQueueGate
{
	private readonly object syncRoot = new();
	private int generation;

	public int CaptureGeneration()
	{
		lock (syncRoot)
			return generation;
	}

	public bool TryRun(int expectedGeneration, CancellationToken cancellationToken, Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		lock (syncRoot)
		{
			if (cancellationToken.IsCancellationRequested || expectedGeneration != generation)
				return false;

			action();
			return true;
		}
	}

	public void Close(CancellationTokenSource? cancellationSource, Action clear)
	{
		ArgumentNullException.ThrowIfNull(clear);

		lock (syncRoot)
		{
			generation++;
			cancellationSource?.Cancel();
			clear();
		}
	}
}
