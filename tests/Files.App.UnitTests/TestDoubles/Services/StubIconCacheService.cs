// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Returns no icons for legacy materialization tests.</summary>
internal sealed class StubIconCacheService : IIconCacheService
{
	/// <summary>Gets the item paths requested by the warm-up queue.</summary>
	public List<string> RequestedPaths { get; } = [];

	/// <inheritdoc />
	public Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
	{
		lock (RequestedPaths)
			RequestedPaths.Add(itemPath);

		return Task.FromResult<byte[]?>(null);
	}

	/// <inheritdoc />
	public void Clear() { }
}
