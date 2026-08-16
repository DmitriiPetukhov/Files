using System.Runtime.CompilerServices;
using Files.App.Utils;
using Files.App.Utils.Storage.Projections;

namespace Files.App.UnitTests.TestDoubles.Utils.Storage.Projections;

/// <summary>Creates isolated folder-item projections for unit tests.</summary>
internal static class FolderItemListedItemProjectionTestFactory
{
	/// <summary>Creates a projection without application-service dependencies.</summary>
	public static FolderItemListedItemProjection Create()
		=> new(
			localize: static resourceKey => resourceKey,
			formatSize: static sizeBytes => $"{sizeBytes} bytes",
			createListedItem: static _ =>
				(ListedItem)RuntimeHelpers.GetUninitializedObject(typeof(ListedItem)));
}
