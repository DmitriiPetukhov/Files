// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading.Tasks;
using Files.App.Utils.Storage.Enumerators.Win32;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators.Win32;

/// <summary>Defers a Win32 folder-open result until a test completes it.</summary>
internal sealed class BlockingWin32FolderOpener : IWin32FolderOpener
{
	private readonly TaskCompletionSource<Win32FolderEnumerationOpenResult> openResultSource =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <inheritdoc />
	public Task<Win32FolderEnumerationOpenResult> OpenAsync(string _)
		=> openResultSource.Task;

	/// <summary>Completes the deferred folder-open operation.</summary>
	public void Complete(Win32FolderEnumerationOpenResult openResult)
		=> openResultSource.SetResult(openResult);
}
