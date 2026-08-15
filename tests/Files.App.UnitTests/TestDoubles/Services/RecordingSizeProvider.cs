// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.SizeProvider;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Records folder-size requests and returns a deterministic size.</summary>
internal sealed class RecordingSizeProvider : ISizeProvider
{
	/// <summary>Gets the paths passed to <see cref="TryGetSize"/>.</summary>
	public List<string> TryGetSizePaths { get; } = [];

	/// <summary>Gets the paths passed to <see cref="UpdateAsync"/>.</summary>
	public List<string> UpdatePaths { get; } = [];

	/// <inheritdoc />
	public event EventHandler<SizeChangedEventArgs>? SizeChanged
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public Task CleanAsync() => Task.CompletedTask;

	/// <inheritdoc />
	public Task ClearAsync() => Task.CompletedTask;

	/// <inheritdoc />
	public Task UpdateAsync(string path, CancellationToken cancellationToken)
	{
		UpdatePaths.Add(path);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public bool TryGetSize(string path, out ulong size)
	{
		TryGetSizePaths.Add(path);
		size = 42;
		return true;
	}

	/// <inheritdoc />
	public void Dispose() { }
}
