// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Navigation;

/// <summary>Describes the result of one Win32 navigation execution.</summary>
internal sealed record Win32NavigationExecutionResult(
	Win32NavigationExecutionStatus Status,
	NavigationUnavailableReason? FailureReason = null,
	string? FailureMessage = null,
	bool OpenTimedOut = false);

/// <summary>Describes how the Win32 navigation executor completed.</summary>
internal enum Win32NavigationExecutionStatus
{
	/// <summary>Enumeration and publication completed.</summary>
	Completed,
	/// <summary>Navigation cancellation stopped execution.</summary>
	Canceled,
	/// <summary>The caller should use its configured fallback enumeration path.</summary>
	Fallback,
	/// <summary>The location could not be opened.</summary>
	Unavailable,
	/// <summary>Enumeration failed after the scope was opened.</summary>
	Failed,
}
