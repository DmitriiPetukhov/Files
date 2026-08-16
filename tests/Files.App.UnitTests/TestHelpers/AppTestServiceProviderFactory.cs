// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App.Data.Contracts;
using Files.App.Services;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.SizeProvider;
using Files.App.UnitTests.TestDoubles.Services;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Files.App.UnitTests.TestHelpers;

/// <summary>Builds the minimal application service graph used by app unit tests.</summary>
internal static class AppTestServiceProviderFactory
{
	/// <summary>Creates and registers the services required by legacy materialization tests.</summary>
	public static ServiceProvider Create(
		StubUserSettingsService settings,
		IconWarmUpQueue iconWarmUpQueue,
		ISizeProvider? sizeProvider = null)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(iconWarmUpQueue);

		var serviceProvider = new ServiceCollection()
			.AddSingleton<IUserSettingsService>(settings)
			.AddSingleton<IFoldersSettingsService>(settings.FoldersSettings)
			.AddSingleton<IStartMenuService, StubStartMenuService>()
			.AddSingleton<IFileTagsSettingsService, StubFileTagsSettingsService>()
			.AddSingleton<IDateTimeFormatter, StubDateTimeFormatter>()
			.AddSingleton<ISizeProvider>(sizeProvider ?? new RecordingSizeProvider())
			.AddSingleton<IStorageCacheService, StorageCacheService>()
			.AddSingleton(iconWarmUpQueue)
			.BuildServiceProvider();

		Ioc.Default.ConfigureServices(serviceProvider);
		return serviceProvider;
	}
}
