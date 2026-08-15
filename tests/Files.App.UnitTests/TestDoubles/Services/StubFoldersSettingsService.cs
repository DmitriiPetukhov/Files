// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using System.ComponentModel;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Stores folder visibility settings needed by legacy materialization tests.</summary>
internal sealed class StubFoldersSettingsService : IFoldersSettingsService
{
	/// <inheritdoc />
	public bool ShowHiddenItems { get; set; }

	/// <inheritdoc />
	public bool ShowProtectedSystemFiles { get; set; }

	/// <inheritdoc />
	public bool AreAlternateStreamsVisible { get; set; }

	/// <inheritdoc />
	public bool ShowDotFiles { get; set; }

	/// <inheritdoc />
	public SingleClickOpenMode OpenFilesWithSingleClick { get; set; }

	/// <inheritdoc />
	public SingleClickOpenMode OpenFoldersWithSingleClick { get; set; }

	/// <inheritdoc />
	public SingleClickOpenMode OpenFoldersInColumnsViewWithSingleClick { get; set; }

	/// <inheritdoc />
	public bool OpenFoldersInNewTab { get; set; }

	/// <inheritdoc />
	public bool CalculateFolderSizes { get; set; }

	/// <inheritdoc />
	public bool ScrollToPreviousFolderWhenNavigatingUp { get; set; }

	/// <inheritdoc />
	public bool ShowFileExtensions { get; set; }

	/// <inheritdoc />
	public bool ShowThumbnails { get; set; }

	/// <inheritdoc />
	public DeleteConfirmationPolicies DeleteConfirmationPolicy { get; set; }

	/// <inheritdoc />
	public bool SelectFilesOnHover { get; set; }

	/// <inheritdoc />
	public bool DoubleClickToGoUp { get; set; }

	/// <inheritdoc />
	public bool ShowFileExtensionWarning { get; set; }

	/// <inheritdoc />
	public bool ShowCheckboxesWhenSelectingItems { get; set; }

	/// <inheritdoc />
	public SizeUnitTypes SizeUnitFormat { get; set; }

	/// <inheritdoc />
	public event PropertyChangedEventHandler? PropertyChanged
	{
		add { }
		remove { }
	}
}
