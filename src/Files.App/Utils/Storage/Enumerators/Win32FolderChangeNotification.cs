// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Files.App.Utils.Storage;

internal enum Win32FolderChangeAction : uint
{
	Unknown = 0,
	Added = 1,
	Removed = 2,
	Modified = 3,
	RenamedOldName = 4,
	RenamedNewName = 5,
}

internal readonly record struct Win32FolderChangeNotification(
	Win32FolderChangeAction Action,
	string FullPath);

internal static class Win32FolderChangeParser
{
	private const int HeaderSize = 12;

	public static IReadOnlyList<Win32FolderChangeNotification> Parse(
		ReadOnlySpan<byte> buffer,
		string folderPath)
	{
		ArgumentException.ThrowIfNullOrEmpty(folderPath);

		if (buffer.IsEmpty)
			return Array.Empty<Win32FolderChangeNotification>();

		var notifications = new List<Win32FolderChangeNotification>();
		var offset = 0;

		while (true)
		{
			if (buffer.Length - offset < HeaderSize)
				throw new FormatException("The native change notification header is truncated.");

			var nextEntryOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4));
			var action = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 4, 4));
			var fileNameLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 8, 4));
			int nextRecordOffset;
			if (nextEntryOffset == 0)
			{
				nextRecordOffset = buffer.Length;
			}
			else
			{
				if (nextEntryOffset % 4 != 0 || nextEntryOffset > (uint)(buffer.Length - offset))
					throw new FormatException("The native change notification offset is invalid.");

				nextRecordOffset = offset + (int)nextEntryOffset;
			}

			var nameOffset = offset + HeaderSize;
			if (fileNameLength == 0 || fileNameLength % 2 != 0 || fileNameLength > nextRecordOffset - nameOffset)
				throw new FormatException("The native change notification name is invalid.");

			var name = Encoding.Unicode.GetString(buffer.Slice(nameOffset, checked((int)fileNameLength)));
			notifications.Add(new(MapAction(action), Path.Combine(folderPath, name)));

			if (nextEntryOffset == 0)
				return notifications;

			offset = nextRecordOffset;
		}
	}

	private static Win32FolderChangeAction MapAction(uint action)
		=> action switch
		{
			1 => Win32FolderChangeAction.Added,
			2 => Win32FolderChangeAction.Removed,
			3 => Win32FolderChangeAction.Modified,
			4 => Win32FolderChangeAction.RenamedOldName,
			5 => Win32FolderChangeAction.RenamedNewName,
			_ => Win32FolderChangeAction.Unknown,
		};
}
