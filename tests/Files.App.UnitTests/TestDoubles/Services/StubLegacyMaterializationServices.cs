// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Enums;
using Files.App.Data.EventArguments;
using Files.App.Services.SizeProvider;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Provides minimal application services for legacy materialization tests.</summary>
internal sealed class StubUserSettingsService : IUserSettingsService
{
	/// <summary>Gets the configurable folder settings used by the test.</summary>
	public StubFoldersSettingsService FoldersSettings { get; } = new();

	/// <inheritdoc />
	IFoldersSettingsService IUserSettingsService.FoldersSettingsService => FoldersSettings;

	/// <inheritdoc />
	public IGeneralSettingsService GeneralSettingsService => null!;

	/// <inheritdoc />
	public IAppearanceSettingsService AppearanceSettingsService => null!;

	/// <inheritdoc />
	public IApplicationSettingsService ApplicationSettingsService => null!;

	/// <inheritdoc />
	public IInfoPaneSettingsService InfoPaneSettingsService => null!;

	/// <inheritdoc />
	public ILayoutSettingsService LayoutSettingsService => null!;

	/// <inheritdoc />
	public IAppSettingsService AppSettingsService => null!;

	/// <inheritdoc />
	public event EventHandler<SettingChangedEventArgs> OnSettingChangedEvent
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public bool ImportSettings(object import) => false;

	/// <inheritdoc />
	public object ExportSettings() => new object();
}

/// <summary>Stores the folder visibility settings needed by legacy materialization.</summary>
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

/// <summary>Returns no icons for legacy materialization tests.</summary>
internal sealed class StubIconCacheService : IIconCacheService
{
	/// <inheritdoc />
	public Task<byte[]?> GetIconAsync(string itemPath, string? extension, bool isFolder)
		=> Task.FromResult<byte[]?>(null);

	/// <inheritdoc />
	public void Clear() { }
}
