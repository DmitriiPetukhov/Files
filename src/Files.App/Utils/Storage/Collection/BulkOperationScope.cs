// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class BulkOperationScope : IDisposable
{
	private readonly Action end;

	public BulkOperationScope(Action begin, Action end)
	{
		ArgumentNullException.ThrowIfNull(begin);
		this.end = end ?? throw new ArgumentNullException(nameof(end));
		begin();
	}

	public void Dispose() => end();
}
