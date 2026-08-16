// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.TestHelpers;

/// <summary>Initializes shared application services required by legacy item tests.</summary>
[TestClass]
public sealed class AppTestAssemblySetup
{
	/// <summary>Configures the application service graph once for the test process.</summary>
	[AssemblyInitialize]
	public static void Initialize(TestContext _)
		=> AppTestServiceProviderFactory.ConfigureDefaultServices();

	/// <summary>Releases the shared application service graph after the test process completes.</summary>
	[AssemblyCleanup]
	public static ValueTask CleanupAsync()
		=> AppTestServiceProviderFactory.DisposeDefaultServicesAsync();
}
