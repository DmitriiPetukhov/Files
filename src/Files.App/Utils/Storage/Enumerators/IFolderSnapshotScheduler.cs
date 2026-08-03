// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

/// <summary>
/// Schedules one snapshot callback on the UI dispatcher.
/// </summary>
internal interface IFolderSnapshotScheduler
{
	/// <summary>
	/// Schedules the callback and reports enqueue or callback failures to the caller.
	/// </summary>
	/// <param name="callback">The callback that applies the current snapshot.</param>
	Task ScheduleAsync(Func<Task> callback);
}
