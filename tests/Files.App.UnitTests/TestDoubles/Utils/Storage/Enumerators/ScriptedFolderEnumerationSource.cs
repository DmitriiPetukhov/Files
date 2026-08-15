// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;

/// <summary>Yields scripted provider-neutral batches for publication tests.</summary>
internal sealed class ScriptedFolderEnumerationSource : IFolderEnumerationSource
{
	private readonly IReadOnlyList<IReadOnlyCollection<FolderItem>> batches;
	private readonly TimeSpan delayBetweenBatches;

	public ScriptedFolderEnumerationSource(
		IReadOnlyList<IReadOnlyCollection<FolderItem>> batches,
		TimeSpan delayBetweenBatches = default)
	{
		this.batches = batches ?? throw new ArgumentNullException(nameof(batches));
		this.delayBetweenBatches = delayBetweenBatches;
	}

	/// <inheritdoc />
	public IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		CancellationToken cancellationToken = default)
		=> EnumerateBatchesAsync(cancellationToken);

	/// <inheritdoc />
	public ValueTask<FolderItem?> ResolveAsync(
		FolderItemKey itemKey,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<FolderItem?>(null);

	/// <inheritdoc />
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateBatchesAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (var index = 0; index < batches.Count; index++)
		{
			if (index > 0 && delayBetweenBatches > TimeSpan.Zero)
				await Task.Delay(delayBetweenBatches, cancellationToken);

			yield return new FolderEnumerationBatch<FolderItem>(batches[index], index);
		}
	}
}
