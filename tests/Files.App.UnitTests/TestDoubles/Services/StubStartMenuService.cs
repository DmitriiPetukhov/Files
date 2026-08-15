// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using OwlCore.Storage;
using System.Threading.Tasks;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Provides deterministic Start Menu state for item materialization tests.</summary>
internal sealed class StubStartMenuService : IStartMenuService
{
	/// <inheritdoc />
	public bool IsPinned(string itemPath) => false;

	/// <inheritdoc />
	public Task<bool> IsPinnedAsync(IStorable storable) => Task.FromResult(false);

	/// <inheritdoc />
	public Task PinAsync(IStorable storable, string? displayName = null) => Task.CompletedTask;

	/// <inheritdoc />
	public Task UnpinAsync(IStorable storable) => Task.CompletedTask;
}
