using Editor.GlobalChat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ChatMentionPolicyTests
{
	[TestMethod]
	[DataRow( "Hello @Alice", "Alice", true )]
	[DataRow( "Hello @Alice.", "Alice", true )]
	[DataRow( "@Alice. Next sentence", "Alice", true )]
	[DataRow( "@Alice.com", "Alice", false )]
	[DataRow( "@aLiCe, hello", "Alice", true )]
	[DataRow( "Hello @Alice Smith!", "Alice Smith", true )]
	[DataRow( "@AliceOther", "Alice", false )]
	[DataRow( "mail@Alice", "Alice", false )]
	[DataRow( "@@Alice", "Alice", false )]
	[DataRow( "No mention", "Alice", false )]
	[DataRow( "@42 hello", "Alice", true )]
	[DataRow( "<@42> hello", "Alice", true )]
	[DataRow( "@420 hello", "Alice", false )]
	public void MatchesCompleteMentions( string text, string name, bool expected )
	{
		Assert.AreEqual( expected, ChatMentionPolicy.ContainsMention( text, 42, name ) );
	}

	[TestMethod]
	public void HistorySelfBlockedAndDisabledMessagesDoNotNotify()
	{
		var gate = new ChatMentionGate();
		Assert.IsFalse( gate.ShouldNotify( Guid.NewGuid(), 7, 42, "Alice", "@Alice", false, false, true ) );
		Assert.IsFalse( gate.ShouldNotify( Guid.NewGuid(), 42, 42, "Alice", "@Alice", true, false, true ) );
		Assert.IsFalse( gate.ShouldNotify( Guid.NewGuid(), 7, 42, "Alice", "@Alice", true, true, true ) );
		var disabled = Guid.NewGuid();
		Assert.IsFalse( gate.ShouldNotify( disabled, 7, 42, "Alice", "@Alice", true, false, false ) );
		Assert.IsFalse( gate.ShouldNotify( disabled, 7, 42, "Alice", "@Alice", true, false, true ) );
		Assert.IsTrue( gate.ShouldNotify( Guid.NewGuid(), 7, 42, "Alice", "@Alice", true, false, true ) );
	}

	[TestMethod]
	public void EachAuthenticatedSenderAndMessagePairNotifiesOnce()
	{
		var gate = new ChatMentionGate();
		var id = Guid.NewGuid();
		Assert.IsTrue( gate.ShouldNotify( id, 7, 42, "Alice", "@Alice", true, false, true ) );
		Assert.IsFalse( gate.ShouldNotify( id, 7, 42, "Alice", "@Alice", true, false, true ) );
		Assert.IsTrue( gate.ShouldNotify( id, 8, 42, "Alice", "@Alice", true, false, true ) );
	}
}
