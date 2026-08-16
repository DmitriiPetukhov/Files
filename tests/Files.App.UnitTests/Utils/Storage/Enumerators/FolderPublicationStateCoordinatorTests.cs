using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.App.UnitTests.TestDoubles.Utils.Storage.Enumerators;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators;

/// <summary>Verifies the worker-level provider-neutral publication stream.</summary>
[TestClass]
public sealed class FolderPublicationStateCoordinatorTests
{
	/// <summary>Ensures the first state is readable while the source remains paused.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PublishesFirstStateBeforeSourceCompletion()
	{
		var firstItem = CreateItem("first");
		var source = new PausedFolderEnumerationSource(
			new FolderEnumerationBatch<FolderItem>([firstItem], 0));
		await using var coordinator = new FolderPublicationCoordinator();
		var states = new List<FolderPublicationState>();
		var readTask = ReadStatesAsync(coordinator, states);
		var enumerateTask = coordinator.EnumerateAsync(source.EnumerateAsync(), CancellationToken.None);

		await source.FirstBatchPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForCountAsync(states, 1);

		Assert.IsFalse(source.EnumerationCompleted);
		Assert.AreEqual(firstItem, states[0].Items.Single());
		Assert.IsFalse(states[0].IsFinal);

		source.Release();
		await enumerateTask;
		await readTask;

		Assert.AreEqual(2, states.Count);
		Assert.IsTrue(states.Single(state => state.IsFinal).IsFinal);
	}

	/// <summary>Ensures source errors complete the state reader with the same error.</summary>
	[TestMethod]
	public async Task EnumerateAsync_PropagatesSourceFailureToReader()
	{
		var failure = new InvalidOperationException("source failed");
		var source = new FailingFolderEnumerationSource(failure);
		await using var coordinator = new FolderPublicationCoordinator();
		var readTask = ReadStatesAsync(coordinator, []);

		var enumerateException = await CaptureExceptionAsync<InvalidOperationException>(() =>
			coordinator.EnumerateAsync(source.EnumerateAsync(), CancellationToken.None));
		var readException = await CaptureExceptionAsync<InvalidOperationException>(() => readTask);

		Assert.AreSame(failure, enumerateException);
		Assert.AreSame(failure, readException);
	}

	/// <summary>Ensures cancellation prevents final publication and late keyed updates.</summary>
	[TestMethod]
	public async Task CancelAsync_StopsPausedSourceAndRejectsLateUpdate()
	{
		var firstItem = CreateItem("first");
		var source = new PausedFolderEnumerationSource(
			new FolderEnumerationBatch<FolderItem>([firstItem], 0));
		await using var coordinator = new FolderPublicationCoordinator();
		var states = new List<FolderPublicationState>();
		var readTask = ReadStatesAsync(coordinator, states);
		var enumerateTask = coordinator.EnumerateAsync(source.EnumerateAsync(), CancellationToken.None);

		await source.FirstBatchPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForCountAsync(states, 1);
		await coordinator.CancelAsync();
		await CaptureExceptionAsync<OperationCanceledException>(() => enumerateTask);
		await readTask;

		Assert.AreEqual(1, states.Count);
		Assert.IsFalse(states.Any(state => state.IsFinal));
		Assert.IsFalse(await coordinator.TryApplyUpdateAsync(
			firstItem.Key,
			CreateItem("late"),
			1,
			CancellationToken.None));
	}

	/// <summary>Ensures an empty source emits only the authoritative empty final state.</summary>
	[TestMethod]
	public async Task EnumerateAsync_EmptySourcePublishesOnlyFinalEmptyState()
	{
		await using var coordinator = new FolderPublicationCoordinator();
		var states = new List<FolderPublicationState>();
		var readTask = ReadStatesAsync(coordinator, states);

		await coordinator.EnumerateAsync(EmptyBatchesAsync(), CancellationToken.None);
		await readTask;

		Assert.AreEqual(1, states.Count);
		Assert.IsTrue(states[0].IsFinal);
		Assert.AreEqual(0, states[0].Items.Length);
	}

	private static async Task ReadStatesAsync(
		FolderPublicationCoordinator coordinator,
		ICollection<FolderPublicationState> states)
	{
		await foreach (var state in coordinator.ReadStates())
			states.Add(state);
	}

	private static async Task WaitForCountAsync<T>(IReadOnlyCollection<T> items, int expectedCount)
	{
		var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (items.Count < expectedCount && DateTime.UtcNow < timeout)
			await Task.Delay(10);

		Assert.AreEqual(expectedCount, items.Count);
	}

	private static FolderItem CreateItem(string key)
		=> new(new FolderItemKey("test", key), key, FolderItemKind.File, null, null);

	private static async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EmptyBatchesAsync()
	{
		await Task.Yield();
		yield break;
	}

	private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action)
		where TException : Exception
	{
		try
		{
			await action();
		}
		catch (TException exception)
		{
			return exception;
		}

		Assert.Fail($"Expected {typeof(TException).Name}.");
		return null!;
	}

	private sealed class FailingFolderEnumerationSource(Exception failure) : IFolderEnumerationSource
	{
		public IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateAsync(
			CancellationToken cancellationToken = default)
			=> EnumerateFailureAsync(cancellationToken);

		public ValueTask<FolderItem?> ResolveAsync(
			FolderItemKey itemKey,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<FolderItem?>(null);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		private async IAsyncEnumerable<FolderEnumerationBatch<FolderItem>> EnumerateFailureAsync(
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.Yield();
			if (cancellationToken.IsCancellationRequested)
				yield break;

			throw failure;
		}
	}
}
