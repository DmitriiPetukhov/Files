// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage;

internal sealed class Win32PublicationGeneration
{
	private long nextGeneration;
	private long activeGeneration;

	public bool IsActive => Volatile.Read(ref activeGeneration) != 0;

	public long Start()
	{
		var generation = Interlocked.Increment(ref nextGeneration);
		Volatile.Write(ref activeGeneration, generation);
		return generation;
	}

	public bool IsCurrent(long generation)
		=> Volatile.Read(ref activeGeneration) == generation;

	public void Complete(long generation)
		=> Interlocked.CompareExchange(ref activeGeneration, 0, generation);
}
