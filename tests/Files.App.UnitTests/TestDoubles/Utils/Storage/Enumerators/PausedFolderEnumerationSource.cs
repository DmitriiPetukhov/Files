using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;

/// <summary>Yields a first folder batch and waits until the test releases source completion.</summary>
internal sealed class PausedFolderEnumerationSource : IFolderEnumerationSource
{
	private readonly FolderEnumerationBatch<FolderItem> firstBatch;
	private readonly FolderEnumerationBatch<FolderItem>? secondBatch;
	private readonly TaskCompletionSource<bool> releaseSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public PausedFolderEnumerationSource(
		FolderEnumerationBatch<FolderItem> firstBatch,
		FolderEnumerationBatch<FolderItem>? secondBatch = null)
	{
		this.firstBatch = firstBatch ?? throw new ArgumentNullException(nameof(firstBatch));
		this.secondBatch = secondBatch;
	}

	public TaskCompletionSource<bool> FirstBatchPublished { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public bool EnumerationCompleted { get; private set; }

	public IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		CancellationToken cancellationToken = default)
		=> EnumerateBatchesAsync(cancellationToken);

	public ValueTask<FolderItem?> ResolveAsync(
		FolderItemKey itemKey,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<FolderItem?>(null);

	public ValueTask DisposeAsync()
	{
		releaseSource.TrySetResult(true);
		return ValueTask.CompletedTask;
	}

	public void Release()
		=> releaseSource.TrySetResult(true);

	private async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateBatchesAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			yield return firstBatch;
			FirstBatchPublished.TrySetResult(true);
			await releaseSource.Task.WaitAsync(cancellationToken);

			if (secondBatch is not null)
				yield return secondBatch;
		}
		finally
		{
			EnumerationCompleted = true;
		}
	}
}
