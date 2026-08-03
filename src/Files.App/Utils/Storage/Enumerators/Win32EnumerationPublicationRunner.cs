// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class Win32EnumerationPublicationRunner<T> : IAsyncDisposable
{
	private readonly List<T> finalItems = new();
	private readonly Win32EnumerationPublicationGate<T> publicationGate;

	public Win32EnumerationPublicationRunner(
		Func<IReadOnlyList<T>, Task> publishAsync,
		int initialBatchSize,
		int intermediateBatchSize,
		TimeSpan batchTimeout,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Action<Exception>? errorHandler = null,
		CancellationToken publicationCancellationToken = default)
	{
		publicationGate = new Win32EnumerationPublicationGate<T>(
			publishAsync,
			initialBatchSize,
			intermediateBatchSize,
			batchTimeout,
			delayAsync,
			errorHandler,
			publicationCancellationToken);
	}

	public Task AddAsync(T item, CancellationToken cancellationToken)
		=> AddAsync(item, countsTowardThreshold: true, cancellationToken);

	public async Task AddAsync(T item, bool countsTowardThreshold, CancellationToken cancellationToken)
	{
		finalItems.Add(item);
		await publicationGate.AddAsync(item, countsTowardThreshold, cancellationToken);
	}

	public async Task<List<T>> CompleteAsync(CancellationToken cancellationToken)
	{
		await publicationGate.CompleteAsync(cancellationToken);
		return finalItems;
	}

	public ValueTask DisposeAsync()
		=> publicationGate.DisposeAsync();
}
