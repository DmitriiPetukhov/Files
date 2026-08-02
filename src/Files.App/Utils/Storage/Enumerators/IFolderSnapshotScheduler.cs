// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal interface IFolderSnapshotScheduler
{
	Task ScheduleAsync(Func<Task> callback);
}
