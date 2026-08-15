// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Navigation;

/// <summary>Tracks disposal while exposing an empty provider-neutral source.</summary>
internal sealed class TrackingFolderEnumerationSource : IFolderEnumerationSource
{
	public int DisposeCount { get; private set; }

	/// <inheritdoc />
	public IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		CancellationToken cancellationToken = default)
		=> EnumerateEmptyAsync(cancellationToken);

	/// <inheritdoc />
	public ValueTask<FolderItem?> ResolveAsync(
		FolderItemKey itemKey,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<FolderItem?>(null);

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		DisposeCount++;
		return ValueTask.CompletedTask;
	}

	private static async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateEmptyAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await Task.CompletedTask;
		yield break;
	}
}
