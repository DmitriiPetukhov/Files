// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Utils.Storage.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Applies cheap Win32 visibility rules before provider-neutral publication.</summary>
internal sealed class Win32FolderPublicationAdapter
{
	private readonly IFolderEnumerationSource source;
	private readonly IFoldersSettingsService foldersSettings;

	/// <summary>Creates a cheap publication adapter over an owned Win32 source.</summary>
	/// <param name="source">Source that owns the native enumeration handle.</param>
	/// <param name="foldersSettings">Current visibility settings.</param>
	public Win32FolderPublicationAdapter(
		IFolderEnumerationSource source,
		IFoldersSettingsService foldersSettings)
	{
		this.source = source ?? throw new ArgumentNullException(nameof(source));
		this.foldersSettings = foldersSettings ?? throw new ArgumentNullException(nameof(foldersSettings));
	}

	/// <summary>Yields accepted source batches without invoking legacy materializers.</summary>
	/// <param name="cancellationToken">Token for the active navigation.</param>
	/// <returns>Non-empty visibility-filtered batches in source order.</returns>
	public async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var batch in source.EnumerateAsync(cancellationToken).WithCancellation(cancellationToken))
		{
			var acceptedItems = new List<FolderItem>(batch.Items.Count);
			foreach (var item in batch.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (IsVisible(item))
					acceptedItems.Add(item);
			}

			if (acceptedItems.Count > 0)
				yield return new FolderEnumerationBatch<FolderItem>(acceptedItems, batch.SequenceNumber);
		}
	}

	private bool IsVisible(FolderItem item)
	{
		if (item.ProviderData is not Win32FolderItemData providerData)
			return true;

		var fileAttributes = (FileAttributes)providerData.FindData.dwFileAttributes;
		var isHidden = fileAttributes.HasFlag(FileAttributes.Hidden);
		var isSystem = fileAttributes.HasFlag(FileAttributes.System);
		var startsWithDot = item.Name.StartsWith(".", StringComparison.Ordinal);

		return (!isHidden ||
			(foldersSettings.ShowHiddenItems && (!isSystem || foldersSettings.ShowProtectedSystemFiles))) &&
			(!startsWithDot || foldersSettings.ShowDotFiles);
	}
}
