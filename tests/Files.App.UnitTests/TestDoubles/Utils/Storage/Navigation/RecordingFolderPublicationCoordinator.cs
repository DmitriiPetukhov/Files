// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Navigation;

/// <summary>Records publication execution and optionally scripts its result.</summary>
internal sealed class RecordingFolderPublicationCoordinator<T> : IFolderPublicationCoordinator<T>
{
	private readonly Func<IFolderEnumerationSource<T>, CancellationToken, Task<IReadOnlyCollection<T>>> enumerate;

	public RecordingFolderPublicationCoordinator(
		Func<IFolderEnumerationSource<T>, CancellationToken, Task<IReadOnlyCollection<T>>>? enumerate = null)
	{
		this.enumerate = enumerate ?? ((_, _) => Task.FromResult<IReadOnlyCollection<T>>(Array.Empty<T>()));
	}

	public bool EnumerateCalled { get; private set; }

	/// <inheritdoc />
	public Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken)
	{
		EnumerateCalled = true;
		return enumerate(source, cancellationToken);
	}

	/// <inheritdoc />
	public Task<bool> TryRebuildIndexAsync(
		IComparer<T> itemComparer,
		CancellationToken cancellationToken)
		=> Task.FromResult(false);

	/// <inheritdoc />
	public Task CancelAsync() => Task.CompletedTask;
}
