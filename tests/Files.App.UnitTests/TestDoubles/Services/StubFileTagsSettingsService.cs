// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Contracts;
using Files.App.Data.Models;
using System;
using System.Collections.Generic;

namespace Files.App.UnitTests.TestDoubles.Services;

/// <summary>Provides empty file-tag settings for item materialization tests.</summary>
internal sealed class StubFileTagsSettingsService : IFileTagsSettingsService
{
	/// <inheritdoc />
	public event EventHandler OnSettingImportedEvent
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public event EventHandler OnTagsUpdated
	{
		add { }
		remove { }
	}

	/// <inheritdoc />
	public IList<TagViewModel> FileTagList { get; set; } = [];

	/// <inheritdoc />
	public TagViewModel GetTagById(string uid) => null!;

	/// <inheritdoc />
	public IList<TagViewModel>? GetTagsByIds(string[] uids) => null;

	/// <inheritdoc />
	public IEnumerable<TagViewModel> GetTagsByName(string tagName) => [];

	/// <inheritdoc />
	public void CreateNewTag(string newTagName, string color) { }

	/// <inheritdoc />
	public void EditTag(string uid, string name, string color) { }

	/// <inheritdoc />
	public void DeleteTag(string uid) { }

	/// <inheritdoc />
	public object ExportSettings() => new object();

	/// <inheritdoc />
	public bool ImportSettings(object import) => false;
}
