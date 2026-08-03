// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Applies a provider-specific batching policy before handing items to publication coordination.
/// </summary>
/// <typeparam name="T">The item type being batched.</typeparam>
internal interface IFolderBatchPublisher<T>
{
	/// <summary>
	/// Adds one item to the pending batch.
	/// </summary>
	/// <param name="item">The accepted item.</param>
	/// <param name="countsTowardPrimaryThreshold">Whether the item advances the primary threshold.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	ValueTask AddAsync(
		T item,
		bool countsTowardPrimaryThreshold,
		CancellationToken cancellationToken);

	/// <summary>
	/// Publishes any remaining completed items and marks the batcher complete.
	/// </summary>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task CompleteAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Stops batching and rejects late items or timer callbacks.
	/// </summary>
	Task CancelAsync();
}
