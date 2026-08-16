// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Utils.Storage.Enumerators.Win32;

/// <summary>Applies cancellation and cleanup policy to a Win32 folder-open operation.</summary>
internal sealed class Win32FolderOpenOperation
{
	private readonly IWin32FolderOpener opener;
	private readonly Action<string, Exception> logFailure;

	/// <summary>Creates a folder-open operation with its native opener and failure logger.</summary>
	internal Win32FolderOpenOperation(
		IWin32FolderOpener opener,
		Action<string, Exception> logFailure)
	{
		this.opener = opener ?? throw new ArgumentNullException(nameof(opener));
		this.logFailure = logFailure ?? throw new ArgumentNullException(nameof(logFailure));
	}

	/// <summary>Opens a folder without waiting indefinitely for an uninterruptible native call.</summary>
	internal async Task<Win32FolderEnumerationOpenResult> TryOpenAsync(
		string path,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (cancellationToken.IsCancellationRequested)
			return CanceledResult();

		var openTask = opener.OpenAsync(path);
		try
		{
			var openResult = await openTask.WaitAsync(cancellationToken);
			if (cancellationToken.IsCancellationRequested)
			{
				await DisposeOpenResultAsync(openResult);
				return CanceledResult();
			}

			return openResult;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = ObserveLateOpenAsync(path, openTask);
			return CanceledResult();
		}
	}

	private static async Task DisposeOpenResultAsync(Win32FolderEnumerationOpenResult openResult)
	{
		if (openResult.Source is not null)
			await openResult.Source.DisposeAsync();
	}

	private async Task ObserveLateOpenAsync(
		string path,
		Task<Win32FolderEnumerationOpenResult> openTask)
	{
		try
		{
			await DisposeOpenResultAsync(await openTask);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logFailure(path, ex);
		}
	}

	private static Win32FolderEnumerationOpenResult CanceledResult()
		=> new(
			Win32FolderEnumerationOpenStatus.Canceled,
			null,
			default,
			0);
}
