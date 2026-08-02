// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;

namespace Files.App.Services
{
	internal sealed class IconCacheService : IIconCacheService
	{
		// Dummy path to generate generic icons for folders, executables, and shortcuts.
		private static readonly string _dummyPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "x46696c6573");

		private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _cache = new();
		private readonly SemaphoreSlim _iconLoadSemaphore = new(4);
		private readonly IIconLoader iconLoader;

		public IconCacheService()
			: this(new FileThumbnailIconLoader())
		{
		}

		internal IconCacheService(IIconLoader iconLoader)
		{
			this.iconLoader = iconLoader ?? throw new ArgumentNullException(nameof(iconLoader));
		}

		public Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		{
			var key = isFolder ? ":folder:" : (extension?.ToLowerInvariant() ?? ":noext:");
			var iconPath = isFolder || string.IsNullOrEmpty(extension) ? _dummyPath : _dummyPath + extension;
			var candidate = new Lazy<Task<byte[]?>>(
				() => LoadIconThroughSemaphoreAsync(iconPath, isFolder),
				LazyThreadSafetyMode.ExecutionAndPublication);
			var entry = _cache.GetOrAdd(key, candidate);

			return AwaitIconAsync(key, entry);
		}

		private async Task<byte[]?> AwaitIconAsync(string key, Lazy<Task<byte[]?>> entry)
		{
			try
			{
				return await entry.Value.ConfigureAwait(false);
			}
			catch
			{
				_cache.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]?>>>(key, entry));
				throw;
			}
		}

		private async Task<byte[]?> LoadIconThroughSemaphoreAsync(string iconPath, bool isFolder)
		{
			await _iconLoadSemaphore.WaitAsync().ConfigureAwait(false);
			try
			{
				// Always use the dummy path so the shell resolves the generic type icon from the
				// extension alone. This works correctly for all path types (local, MTP, FTP, network,
				// cloud, etc.) because the cache is keyed by extension anyway, not by item identity.
				return await iconLoader.LoadAsync(iconPath, isFolder).ConfigureAwait(false);
			}
			finally
			{
				_iconLoadSemaphore.Release();
			}
		}

		public void Clear()
		{
			_cache.Clear();
		}
	}
}
