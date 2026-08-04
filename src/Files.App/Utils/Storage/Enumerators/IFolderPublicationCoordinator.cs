// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Coordinates source enumeration, canonical state, and snapshot publication for one navigation.
/// </summary>
/// <typeparam name="T">The item type shown by the folder projection.</typeparam>
internal interface IFolderPublicationCoordinator<T>
{
	/// <summary>
	/// Runs the source and publishes intermediate and final snapshots.
	/// </summary>
	/// <param name="source">The provider-neutral enumeration source.</param>
	/// <param name="cancellationToken">The token for the current navigation.</param>
	/// <returns>The authoritative complete result.</returns>
	Task<IReadOnlyCollection<T>> EnumerateAsync(
		IFolderEnumerationSource<T> source,
		CancellationToken cancellationToken);

}
