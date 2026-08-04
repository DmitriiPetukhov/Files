// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Serializes publication-session mutation, snapshot application, and related operations.
/// </summary>
internal sealed class FolderPublicationOperationGate
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
