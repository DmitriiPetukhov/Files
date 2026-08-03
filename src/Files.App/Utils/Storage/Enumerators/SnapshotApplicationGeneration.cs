// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class SnapshotApplicationGeneration
{
	private long nextGeneration;
	private long currentGeneration;

	public long Start()
	{
		var generation = Interlocked.Increment(ref nextGeneration);
		Volatile.Write(ref currentGeneration, generation);
		return generation;
	}

	public void Invalidate()
	{
		var generation = Interlocked.Increment(ref nextGeneration);
		Volatile.Write(ref currentGeneration, generation);
	}

	public bool IsCurrent(long generation)
		=> Volatile.Read(ref currentGeneration) == generation;
}
