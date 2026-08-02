// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage;

namespace Files.App.Services
{
	internal interface IIconLoader
	{
		Task<byte[]?> LoadAsync(string iconPath, bool isFolder);
	}

	internal sealed class FileThumbnailIconLoader : IIconLoader
	{
		public Task<byte[]?> LoadAsync(string iconPath, bool isFolder)
			=> FileThumbnailHelper.GetIconAsync(
				iconPath,
				Constants.ShellIconSizes.Jumbo,
				isFolder,
				IconOptions.ReturnIconOnly);
	}
}
