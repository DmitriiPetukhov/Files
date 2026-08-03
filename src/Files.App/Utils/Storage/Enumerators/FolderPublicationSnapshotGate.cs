// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Serializes publication-session mutation and snapshot application.
/// </summary>
internal sealed class FolderPublicationSnapshotGate
{
	private readonly SemaphoreSlim semaphore = new(1, 1);

	public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation)
	{
		ArgumentNullException.ThrowIfNull(operation);

		await semaphore.WaitAsync();
		try
		{
			return await operation();
		}
		finally
		{
			semaphore.Release();
		}
	}
}
