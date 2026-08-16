// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using Files.App.Utils.Storage.Contracts;

namespace Files.App.Utils.Storage;

/// <summary>Represents one immutable provider-neutral folder publication state.</summary>
internal sealed record FolderPublicationState
{
	/// <summary>Creates a publication state with the supplied version and ordered items.</summary>
	/// <param name="version">Monotonic state version.</param>
	/// <param name="items">Full source-ordered immutable item snapshot.</param>
	/// <param name="isFinal">Whether the state is the terminal compatibility state.</param>
	public FolderPublicationState(long version, ImmutableArray<FolderItem> items, bool isFinal = false)
	{
		if (version < 0)
			throw new ArgumentOutOfRangeException(nameof(version));

		Version = version;
		Items = items.IsDefault ? ImmutableArray<FolderItem>.Empty : items;
		IsFinal = isFinal;
	}

	/// <summary>Gets the monotonic publication version.</summary>
	public long Version { get; }

	/// <summary>Gets the full immutable source-ordered item snapshot.</summary>
	public ImmutableArray<FolderItem> Items { get; }

	/// <summary>Gets whether this is the terminal compatibility state.</summary>
	public bool IsFinal { get; init; }
}
