// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.DateTimeFormatter;
using Files.App.Data.Enums;
using System;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Formats item timestamps without external application services.</summary>
internal sealed class StubDateTimeFormatter : IDateTimeFormatter
{
	/// <inheritdoc />
	public string Name => "Stub";

	/// <inheritdoc />
	public string ToShortLabel(DateTimeOffset offset) => offset.ToString("O");

	/// <inheritdoc />
	public string ToLongLabel(DateTimeOffset offset) => offset.ToString("O");

	/// <inheritdoc />
	public ITimeSpanLabel ToTimeSpanLabel(DateTimeOffset offset, GroupByDateUnit unit) => null!;
}
