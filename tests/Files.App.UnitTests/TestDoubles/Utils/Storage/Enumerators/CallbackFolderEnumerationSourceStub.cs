using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Files.App.Utils.Storage;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;

/// <summary>Provides sequential callback-based enumeration batches.</summary>
internal sealed class CallbackFolderEnumerationSourceStub<T>(
	IReadOnlyList<IReadOnlyCollection<T>> batches,
	IReadOnlyCollection<T> finalItems) : IFolderEnumerationSource<T>
{
	/// <summary>Publishes configured batches and returns the final items.</summary>
	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		foreach (var batch in batches)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await publishBatchAsync(batch);
		}

		return finalItems;
	}
}
