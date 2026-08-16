// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Files.App.Data.Contracts;
using Files.App.Services;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.SizeProvider;
using Files.App.UnitTests.TestDoubles.Services;
using Files.App.Utils;
using Files.App.Utils.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.App.UnitTests.TestHelpers;

/// <summary>Builds the minimal application service graph used by app unit tests.</summary>
internal static class AppTestServiceProviderFactory
{
	private static readonly object syncRoot = new();
	private static ServiceProvider? defaultServiceProvider;

	/// <summary>Builds the services required by a legacy materialization test.</summary>
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

		return serviceProvider;
	}

	/// <summary>Configures the process-wide application services once before unit tests run.</summary>
	public static void ConfigureDefaultServices()
	{
		lock (syncRoot)
		{
			if (defaultServiceProvider is not null)
				return;

			var settings = new StubUserSettingsService();
			var iconWarmUpQueue = new IconWarmUpQueue(
				new StubIconCacheService(),
				NullLogger<IconWarmUpQueue>.Instance,
				capacity: 1,
				workerCount: 1);
			defaultServiceProvider = Create(settings, iconWarmUpQueue);
			Ioc.Default.ConfigureServices(defaultServiceProvider);
		}
	}

	/// <summary>Disposes the process-wide application services after unit tests complete.</summary>
	public static async ValueTask DisposeDefaultServicesAsync()
	{
		ServiceProvider? serviceProvider;
		lock (syncRoot)
		{
			serviceProvider = defaultServiceProvider;
			defaultServiceProvider = null;
		}

		if (serviceProvider is not null)
			await serviceProvider.DisposeAsync();
	}
}
