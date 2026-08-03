// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class FinalListFolderEnumerationSource<T> : IFolderEnumerationSource<T>
{
	private readonly Func<CancellationToken, Task<IReadOnlyCollection<T>>> enumerateAsync;

	public FinalListFolderEnumerationSource(Func<CancellationToken, Task<IReadOnlyCollection<T>>> enumerateAsync)
	{
		this.enumerateAsync = enumerateAsync ?? throw new ArgumentNullException(nameof(enumerateAsync));
	}

	public async Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(publishBatchAsync);

		var items = await enumerateAsync(cancellationToken);
		return items ?? Array.Empty<T>();
	}
}
