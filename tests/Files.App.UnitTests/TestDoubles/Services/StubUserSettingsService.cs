// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.EventArguments;
using System;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Provides minimal application settings for legacy materialization tests.</summary>
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
