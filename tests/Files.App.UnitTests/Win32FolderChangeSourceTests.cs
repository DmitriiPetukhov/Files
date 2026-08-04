using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Files.App.Utils.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests;

[TestClass]
public sealed class Win32FolderChangeSourceTests
{
	[TestMethod]
	public void Parse_ReturnsOrderedNotificationsWithFullPaths()
	{
		var buffer = CreateBuffer(
			(Win32FolderChangeAction.Added, "one.txt"),
			(Win32FolderChangeAction.RenamedOldName, "old.txt"),
			(Win32FolderChangeAction.RenamedNewName, "new.txt"));

		var notifications = Win32FolderChangeParser.Parse(buffer, @"C:\Folder");

		Assert.AreEqual(3, notifications.Count);
		Assert.AreEqual(Win32FolderChangeAction.Added, notifications[0].Action);
		Assert.AreEqual(@"C:\Folder\one.txt", notifications[0].FullPath);
		Assert.AreEqual(Win32FolderChangeAction.RenamedOldName, notifications[1].Action);
		Assert.AreEqual(@"C:\Folder\old.txt", notifications[1].FullPath);
		Assert.AreEqual(Win32FolderChangeAction.RenamedNewName, notifications[2].Action);
		Assert.AreEqual(@"C:\Folder\new.txt", notifications[2].FullPath);
	}

	[TestMethod]
	public void Parse_RejectsInvalidRecordOffset()
	{
		var buffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 1);

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	[TestMethod]
	public void Parse_RejectsTruncatedFileName()
	{
		var buffer = CreateBuffer((Win32FolderChangeAction.Added, "one.txt"));
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), uint.MaxValue);

		AssertFormatException(() => Win32FolderChangeParser.Parse(buffer, @"C:\Folder"));
	}

	private static byte[] CreateBuffer(params (Win32FolderChangeAction Action, string Name)[] records)
	{
		var encodedNames = new List<byte[]>();
		var totalLength = 0;

		foreach (var record in records)
		{
			var encodedName = Encoding.Unicode.GetBytes(record.Name);
			encodedNames.Add(encodedName);
			totalLength += AlignToFourBytes(12 + encodedName.Length);
		}

		var buffer = new byte[totalLength];
		var offset = 0;
		for (var index = 0; index < records.Length; index++)
		{
			var recordLength = AlignToFourBytes(12 + encodedNames[index].Length);
			var nextOffset = index == records.Length - 1 ? 0 : recordLength;

			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), (uint)nextOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4, 4), (uint)records[index].Action);
			BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 8, 4), (uint)encodedNames[index].Length);
			encodedNames[index].CopyTo(buffer, offset + 12);
			offset += recordLength;
		}

		return buffer;
	}

	private static int AlignToFourBytes(int value)
		=> (value + 3) & ~3;

	private static void AssertFormatException(Action action)
	{
		try
		{
			action();
			Assert.Fail("Expected a FormatException.");
		}
		catch (FormatException)
		{
		}
	}
}
