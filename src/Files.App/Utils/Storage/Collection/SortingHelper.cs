// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Windows.Storage;

namespace Files.App.Utils.Storage
{
	public static class SortingHelper
	{
		private static object OrderByNameFunc(ListedItem item)
			=> item.Name;

		public static Func<ListedItem, object> GetSortFunc(SortOption directorySortOption)
		{
			return directorySortOption switch
			{
				SortOption.Name => item => item.Name,
				SortOption.DateModified => item => item.ItemDateModifiedReal,
				SortOption.DateCreated => item => item.ItemDateCreatedReal,
				SortOption.FileType => item => item.ItemType,
				SortOption.Size => item => item.FileSizeBytes,
				SortOption.SyncStatus => item => item.SyncStatusString,
				SortOption.FileTag => item => item.FileTags?.FirstOrDefault(),
				SortOption.Path => item => item.ItemPath,
				SortOption.OriginalFolder => item => (item as RecycleBinItem)?.ItemOriginalFolder,
				SortOption.DateDeleted => item => (item as RecycleBinItem)?.ItemDateDeletedReal,
				_ => item => item.Name,
			};
		}

		public static IEnumerable<ListedItem> OrderFileList(IList<ListedItem> filesAndFolders, SortOption directorySortOption, SortDirection directorySortDirection,
			bool sortDirectoriesAlongsideFiles, bool sortFilesFirst)
		{
			return filesAndFolders.OrderBy(item => item, GetComparer(directorySortOption, directorySortDirection,
				sortDirectoriesAlongsideFiles, sortFilesFirst));
		}

		public static IComparer<ListedItem> GetComparer(SortOption directorySortOption, SortDirection directorySortDirection,
			bool sortDirectoriesAlongsideFiles, bool sortFilesFirst)
		{
			return new ListedItemComparer(directorySortOption, directorySortDirection, sortDirectoriesAlongsideFiles, sortFilesFirst);
		}

		private sealed class ListedItemComparer : IComparer<ListedItem>
		{
			private readonly Func<ListedItem, object> orderFunc;
			private readonly IComparer<object> naturalStringComparer = NaturalStringComparer.GetForProcessor();
			private readonly SortOption directorySortOption;
			private readonly int directionMultiplier;
			private readonly bool sortDirectoriesAlongsideFiles;
			private readonly bool sortFilesFirst;

			public ListedItemComparer(SortOption directorySortOption, SortDirection directorySortDirection,
				bool sortDirectoriesAlongsideFiles, bool sortFilesFirst)
			{
				this.directorySortOption = directorySortOption;
				this.sortDirectoriesAlongsideFiles = sortDirectoriesAlongsideFiles;
				this.sortFilesFirst = sortFilesFirst;
				this.directionMultiplier = directorySortDirection == SortDirection.Ascending ? 1 : -1;
				orderFunc = GetSortFunc(directorySortOption);
			}

			public int Compare(ListedItem? x, ListedItem? y)
			{
				if (ReferenceEquals(x, y))
					return 0;

				if (x is null)
					return -1;

				if (y is null)
					return 1;

				if (!sortDirectoriesAlongsideFiles)
				{
					var priorityComparison = CompareAscending(PrioritizeFilesOrFolders(x), PrioritizeFilesOrFolders(y));
					if (priorityComparison != 0)
						return priorityComparison;
				}

				if (directorySortOption == SortOption.FileTag)
				{
					var emptyTagComparison = CompareAscending(string.IsNullOrEmpty(orderFunc(x) as string), string.IsNullOrEmpty(orderFunc(y) as string));
					if (emptyTagComparison != 0)
						return emptyTagComparison;
				}

				var sortComparison = Comparer<object>.Default.Compare(orderFunc(x), orderFunc(y)) * directionMultiplier;
				if (sortComparison != 0)
					return sortComparison;

				if (directorySortOption != SortOption.Name)
				{
					var nameComparison = naturalStringComparer.Compare(OrderByNameFunc(x), OrderByNameFunc(y)) * directionMultiplier;
					if (nameComparison != 0)
						return nameComparison;
				}

				// ImmutableSortedSet treats comparer equality as a duplicate, so equal sort keys
				// need a deterministic identity tie-breaker to retain distinct items.
				return CompareIdentity(x, y);
			}

			private bool PrioritizeFilesOrFolders(ListedItem listedItem)
				=> (listedItem.PrimaryItemAttribute == StorageItemTypes.File || listedItem.IsShortcut || listedItem.IsArchive) ^ sortFilesFirst;

			private static int CompareAscending(bool x, bool y)
				=> x.CompareTo(y);

			private static int CompareIdentity(ListedItem x, ListedItem y)
			{
				var comparison = StringComparer.OrdinalIgnoreCase.Compare(x.ItemPath, y.ItemPath);
				if (comparison != 0)
					return comparison;

				comparison = StringComparer.Ordinal.Compare(x.ItemPath, y.ItemPath);
				if (comparison != 0)
					return comparison;

				comparison = StringComparer.Ordinal.Compare(x.GetType().FullName, y.GetType().FullName);
				if (comparison != 0)
					return comparison;

				return RuntimeHelpers.GetHashCode(x).CompareTo(RuntimeHelpers.GetHashCode(y));
			}
		}
	}
}
