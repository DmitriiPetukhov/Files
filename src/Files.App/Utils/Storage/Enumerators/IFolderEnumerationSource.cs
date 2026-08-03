// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal interface IFolderEnumerationSource<T>
{
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken);
}
