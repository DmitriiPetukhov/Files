// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Files.App.Utils.Storage;

/// <summary>
/// Applies source-independent snapshots to the UI projection.
/// </summary>
/// <typeparam name="T">The item type shown by the projection.</typeparam>
internal interface IFolderSnapshotProjection<T>
{
	/// <summary>
	/// Applies one snapshot through a bounded bulk UI operation.
	/// </summary>
	/// <param name="snapshot">The ordered snapshot to display.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	Task ApplyAsync(
		ImmutableArray<T> snapshot,
		CancellationToken cancellationToken);
}
