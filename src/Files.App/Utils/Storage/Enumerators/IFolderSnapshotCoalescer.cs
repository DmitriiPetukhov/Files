// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Coalesces worker-produced snapshots before they are scheduled for UI application.
/// </summary>
/// <typeparam name="T">The item type in each snapshot.</typeparam>
internal interface IFolderSnapshotCoalescer<T>
{
	/// <summary>
	/// Submits an intermediate snapshot, allowing newer snapshots to replace it.
	/// </summary>
	/// <param name="snapshot">The ordered snapshot.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	void Submit(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	/// <summary>
	/// Submits an authoritative final snapshot that bypasses intermediate throttling.
	/// </summary>
	/// <param name="snapshot">The final snapshot.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	void SubmitFinal(
		IReadOnlyCollection<T> snapshot,
		CancellationToken cancellationToken);

	/// <summary>
	/// Waits for scheduled application and optionally retries the pending final snapshot.
	/// </summary>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <param name="retryPendingSnapshot">Whether one final retry is permitted.</param>
	Task DrainAsync(
		CancellationToken cancellationToken,
		bool retryPendingSnapshot = false);

	/// <summary>
	/// Cancels pending scheduling and completes owned work as canceled navigation flow.
	/// </summary>
	Task CancelAsync();
}
