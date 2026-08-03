// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Runs expensive publication-session index work away from the caller's context.
/// </summary>
internal static class FolderPublicationSessionWorker
{
	public static Task<(bool Accepted, IReadOnlyCollection<T>? Snapshot)> RebuildIndexAsync<T>(
		IFolderPublicationSession<T> session,
		IComparer<T> itemComparer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(itemComparer);

		return Task.Run(() =>
		{
			var accepted = session.TryRebuildIndex(itemComparer, cancellationToken, out var snapshot);
			return (accepted, snapshot);
		});
	}
}
