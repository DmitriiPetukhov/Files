using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;

/// <summary>Provides concurrently published enumeration batches.</summary>
internal sealed class ConcurrentFolderEnumerationSourceStub<T>(
	IReadOnlyList<IReadOnlyCollection<T>> batches,
	IReadOnlyCollection<T> finalItems) : IFolderEnumerationSource<T>
{
	/// <summary>Publishes configured batches concurrently and returns the final items.</summary>
	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		var publicationTasks = batches
			.Select(publishBatchAsync)
			.ToArray();

		await Task.WhenAll(publicationTasks);
		return finalItems;
	}
}
