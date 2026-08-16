// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Transitional callback-based source contract used by the existing publication path.
/// </summary>
/// <typeparam name="T">The item type produced by the source.</typeparam>
internal interface IFolderEnumerationSource<T>
{
	/// <summary>Enumerates items while publishing completed batches to the legacy consumer.</summary>
	/// <param name="publishBatchAsync">Callback that receives each completed batch.</param>
	/// <param name="cancellationToken">Token used to stop the enumeration.</param>
	/// <returns>The complete result produced by the source.</returns>
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		Func<IReadOnlyCollection<T>, Task> publishBatchAsync,
		CancellationToken cancellationToken);
}
