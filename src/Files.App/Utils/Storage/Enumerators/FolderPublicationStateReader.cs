// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;
using Files.App.Utils.Storage.Projections;

namespace Files.App.Utils.Storage;

/// <summary>Projects immutable provider-neutral states and submits sorted UI snapshots downstream.</summary>
internal sealed class FolderPublicationStateReader
{
	private readonly object syncRoot = new();
	private readonly FolderItemListedItemProjection projection;
	private readonly IFolderSnapshotCoalescer<ListedItem> coalescer;
	private IComparer<ListedItem> itemComparer;
	private FolderPublicationState? latestState;

	/// <summary>Creates a state reader with the current compatibility projection and comparer.</summary>
	/// <param name="projection">Navigation-scoped provider-to-legacy projection.</param>
	/// <param name="itemComparer">Worker-side UI item comparer.</param>
	/// <param name="coalescer">Downstream latest-wins UI coalescer.</param>
	public FolderPublicationStateReader(
		FolderItemListedItemProjection projection,
		IComparer<ListedItem> itemComparer,
		IFolderSnapshotCoalescer<ListedItem> coalescer)
	{
		this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
		this.itemComparer = itemComparer ?? throw new ArgumentNullException(nameof(itemComparer));
		this.coalescer = coalescer ?? throw new ArgumentNullException(nameof(coalescer));
	}

	/// <summary>Consumes every state and waits for the final UI snapshot to drain.</summary>
	/// <param name="states">Single-reader immutable state stream.</param>
	/// <param name="cancellationToken">Token for the active navigation.</param>
	public async Task ConsumeAsync(
		IAsyncEnumerable<FolderPublicationState> states,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(states);

		try
		{
			await foreach (var state in states.WithCancellation(cancellationToken))
			{
				IComparer<ListedItem> comparer;
				lock (syncRoot)
				{
					latestState = state;
					comparer = itemComparer;
				}

				var snapshot = ProjectAndOrder(state, comparer);
				if (state.IsFinal)
					coalescer.SubmitFinal(snapshot, cancellationToken);
				else
					coalescer.Submit(snapshot, cancellationToken);
			}

			await coalescer.DrainAsync(cancellationToken, retryPendingSnapshot: true).ConfigureAwait(false);
		}
		catch
		{
			await coalescer.CancelAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>Reprojects the latest state for a same-folder sort change.</summary>
	/// <param name="itemComparer">New worker-side item comparer.</param>
	/// <param name="cancellationToken">Token for the active navigation.</param>
	/// <returns><see langword="true"/> when a state was available for reprojection.</returns>
	public Task<bool> TryRebuildIndexAsync(
		IComparer<ListedItem> itemComparer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(itemComparer);

		return Task.Run(() =>
		{
			FolderPublicationState? state;
			lock (syncRoot)
			{
				this.itemComparer = itemComparer;
				state = latestState;
			}

			if (state is null || cancellationToken.IsCancellationRequested)
				return false;

			var snapshot = ProjectAndOrder(state, itemComparer);
			if (state.IsFinal)
				coalescer.SubmitFinal(snapshot, cancellationToken);
			else
				coalescer.Submit(snapshot, cancellationToken);

			return true;
		});
	}

	private IReadOnlyCollection<ListedItem> ProjectAndOrder(
		FolderPublicationState state,
		IComparer<ListedItem> itemComparer)
		=> state.Items.Length == 0
			? Array.Empty<ListedItem>()
			: projection.ProjectState(state).OrderBy(item => item, itemComparer).ToArray();
}
