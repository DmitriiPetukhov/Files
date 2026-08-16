using System;
using System.IO;

namespace Files.App.UnitTests.TestHelpers;

/// <summary>Creates an isolated directory under the system temporary directory.</summary>
internal sealed class TemporaryTestDirectory : IDisposable
{
	/// <summary>Creates and owns a unique temporary test directory.</summary>
	public TemporaryTestDirectory()
	{
		DirectoryPath = Path.Combine(Path.GetTempPath(), $"FilesUnitTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(DirectoryPath);
	}

	/// <summary>Gets the path of the temporary test directory.</summary>
	public string DirectoryPath { get; }

	/// <summary>Removes the temporary test directory and its contents.</summary>
	public void Dispose()
	{
		if (Directory.Exists(DirectoryPath))
			Directory.Delete(DirectoryPath, recursive: true);
	}
}
