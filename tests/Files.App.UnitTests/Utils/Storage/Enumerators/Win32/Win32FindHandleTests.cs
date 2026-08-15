// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using Files.App.Helpers;
using Files.App.Utils.Storage.Enumerators.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators.Win32;

/// <summary>Verifies native Win32 find handle validation.</summary>
[TestClass]
public sealed class Win32FindHandleTests
{
	/// <summary>Ensures a null native handle is rejected.</summary>
	[TestMethod]
	public void Constructor_RejectsNullHandle()
	{
		var exception = CaptureException<ArgumentException>(() => new Win32FindHandle(IntPtr.Zero));

		Assert.AreEqual("handle", exception.ParamName);
	}

	/// <summary>Ensures the invalid native handle value is rejected.</summary>
	[TestMethod]
	public void Constructor_RejectsInvalidHandleValue()
	{
		var exception = CaptureException<ArgumentException>(
			() => new Win32FindHandle(new IntPtr(Win32PInvoke.INVALID_HANDLE_VALUE)));

		Assert.AreEqual("handle", exception.ParamName);
	}

	private static TException CaptureException<TException>(Action action)
		where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null;
	}
}
