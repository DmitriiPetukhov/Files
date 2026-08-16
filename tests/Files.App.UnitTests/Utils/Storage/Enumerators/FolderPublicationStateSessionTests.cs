using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Files.App.Utils.Storage;
using Files.App.Utils.Storage.Contracts;
using Files.App.Utils.Storage.Enumerators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.UnitTests.Utils.Storage.Enumerators;

/// <summary>Verifies provider-neutral keyed publication state transitions.</summary>
[TestClass]
public sealed class FolderPublicationStateSessionTests
{
	/// <summary>Ensures the first accepted batch is published without source completion.</summary>
	[TestMethod]
	public void TryAppend_PublishesFirstAccumulatedStateImmediately()
	{
		var session = new FolderPublicationSession();
		var firstItem = CreateItem("first");
		var batch = new FolderEnumerationBatch<FolderItem>([firstItem], 0);

		Assert.IsTrue(session.TryAppend(batch, CancellationToken.None, out var state));

		Assert.IsNotNull(state);
		Assert.AreEqual(1L, state!.Version);
		Assert.IsFalse(state.IsFinal);
		CollectionAssert.AreEqual(new[] { firstItem }, state.Items.ToArray());
	}

	/// <summary>Ensures later accepted batches create full accumulated source-ordered states.</summary>
	[TestMethod]
	public void TryAppend_AccumulatesItemsInSourceOrder()
	{
		var session = new FolderPublicationSession();
		var firstItem = CreateItem("first");
		var secondItem = CreateItem("second");

		session.TryAppend(new FolderEnumerationBatch<FolderItem>([firstItem], 0), CancellationToken.None, out var firstState);
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([secondItem], 1), CancellationToken.None, out var secondState);

		CollectionAssert.AreEqual(new[] { firstItem }, firstState!.Items.ToArray());
		CollectionAssert.AreEqual(new[] { firstItem, secondItem }, secondState!.Items.ToArray());
		Assert.AreEqual(2L, secondState.Version);
	}

	/// <summary>Ensures an immutable state remains stable after later session mutations.</summary>
	[TestMethod]
	public void TryAppend_DoesNotMutateEarlierImmutableState()
	{
		var session = new FolderPublicationSession();
		var firstItem = CreateItem("first");
		var replacement = CreateItem("replacement");

		session.TryAppend(new FolderEnumerationBatch<FolderItem>([firstItem], 0), CancellationToken.None, out var firstState);
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([replacement], 1), CancellationToken.None, out _);

		CollectionAssert.AreEqual(new[] { firstItem }, firstState!.Items.ToArray());
	}

	/// <summary>Ensures duplicate keys replace in place and advance the item revision.</summary>
	[TestMethod]
	public void TryAppend_ReplacesDuplicateKeyInPlace()
	{
		var session = new FolderPublicationSession();
		var original = CreateItem("same", "original");
		var replacement = CreateItem("same", "replacement");
		var other = CreateItem("other");

		session.TryAppend(new FolderEnumerationBatch<FolderItem>([original, other], 0), CancellationToken.None, out _);
		Assert.IsTrue(session.TryAppend(new FolderEnumerationBatch<FolderItem>([replacement], 1), CancellationToken.None, out var state));

		CollectionAssert.AreEqual(new[] { replacement, other }, state!.Items.ToArray());
		Assert.IsTrue(session.TryGetRevision(original.Key, out var revision));
		Assert.AreEqual(2L, revision);
	}

	/// <summary>Ensures keyed updates require the currently captured revision.</summary>
	[TestMethod]
	public void TryApplyUpdate_RejectsStaleRevisionAndAcceptsExpectedRevision()
	{
		var session = new FolderPublicationSession();
		var original = CreateItem("same", "original");
		var update = CreateItem("same", "update");
		var stale = CreateItem("same", "stale");
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([original], 0), CancellationToken.None, out _);

		Assert.IsTrue(session.TryGetRevision(original.Key, out var revision));
		Assert.IsTrue(session.TryApplyUpdate(original.Key, update, revision, CancellationToken.None, out var updatedState));
		Assert.IsFalse(session.TryApplyUpdate(original.Key, stale, revision, CancellationToken.None, out var staleState));

		Assert.IsNotNull(updatedState);
		Assert.IsNull(staleState);
		Assert.AreEqual(update, updatedState!.Items.Single());
		Assert.AreEqual(update, session.GetCurrentState().Items.Single());
	}

	/// <summary>Ensures unknown keys and canceled sessions cannot publish updates.</summary>
	[TestMethod]
	public void TryApplyUpdate_RejectsUnknownKeyAndCancellation()
	{
		var session = new FolderPublicationSession();
		var item = CreateItem("known");
		var unknown = CreateItem("unknown");
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([item], 0), CancellationToken.None, out _);

		Assert.IsFalse(session.TryApplyUpdate(unknown.Key, unknown, 1, CancellationToken.None, out var unknownState));
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		Assert.IsFalse(session.TryApplyUpdate(item.Key, unknown, 1, cancellationTokenSource.Token, out var canceledState));

		Assert.IsNull(unknownState);
		Assert.IsNull(canceledState);
		Assert.AreEqual(item, session.GetCurrentState().Items.Single());
	}

	/// <summary>Ensures completion is idempotent and rejects all later mutation.</summary>
	[TestMethod]
	public void Complete_IsIdempotentAndRejectsLateMutation()
	{
		var session = new FolderPublicationSession();
		var item = CreateItem("item");
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([item], 0), CancellationToken.None, out _);

		session.Complete();
		session.Complete();

		Assert.IsFalse(session.TryAppend(new FolderEnumerationBatch<FolderItem>([CreateItem("late")], 1), CancellationToken.None, out var appendState));
		Assert.IsFalse(session.TryApplyUpdate(item.Key, CreateItem("late"), 1, CancellationToken.None, out var updateState));
		Assert.IsNull(appendState);
		Assert.IsNull(updateState);
		Assert.AreEqual(1L, session.GetCurrentState().Version);
	}

	/// <summary>Ensures cancellation rejects mutations while retaining the last stable state.</summary>
	[TestMethod]
	public void Cancel_RejectsLateMutationAndPreservesCurrentState()
	{
		var session = new FolderPublicationSession();
		var item = CreateItem("item");
		session.TryAppend(new FolderEnumerationBatch<FolderItem>([item], 0), CancellationToken.None, out _);

		session.Cancel();
		session.Cancel();

		Assert.IsFalse(session.TryAppend(new FolderEnumerationBatch<FolderItem>([CreateItem("late")], 1), CancellationToken.None, out var state));
		Assert.IsNull(state);
		Assert.AreEqual(item, session.GetCurrentState().Items.Single());
	}

	private static FolderItem CreateItem(string key, string? name = null)
		=> new(new FolderItemKey("test", key), name ?? key, FolderItemKind.File, null, null);
}
