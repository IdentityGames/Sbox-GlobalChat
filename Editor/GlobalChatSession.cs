// Adapted from s&box menu GlobalChatService. Keep the message and history formats compatible.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;
using System.Text;
using System.Text.Json;

namespace Editor.GlobalChat;

/// <summary>
/// Global chat membership exists while its view is open, with recent history shared through the lobby.
/// </summary>
internal sealed class GlobalChatSession : IDisposable
{
	readonly EngineChatBridge bridge = new();
	CancellationTokenSource cancellation = new();
	bool disposed;
	internal event Action<ChatLine> LiveMessageReceived;
	internal IDisposable EnterMenuScope() => bridge.EnterMenuScope();
	internal Task JoinPartyAsync( ulong id ) => bridge.JoinPartyAsync( id, cancellation.Token );
	internal ChatChannel Channel { get; private set; }
	internal List<ChatLine> Messages { get; } = new();
	internal bool Connecting { get; private set; }
	internal string Error { get; private set; }
	internal int Revision { get; private set; }
	internal int InvitationRevision { get; private set; }
	internal bool Visible { get; private set; }
	internal string Draft { get; set; } = "";

	/// <summary>
	/// Whether chat is connected and the shared message cooldown has expired.
	/// </summary>
	internal bool CanSend => Channel is not null && RealTime.Now - lastSent >= 1;

	/// <summary>
	/// Whether the party leader can post another invitation, including the shared message cooldown.
	/// </summary>
	internal bool CanInviteParty => CanSend && PartyRoom.Current is { Owner.IsMe: true } && RealTime.Now - lastInvite >= 10;

	internal record ChatLine( Friend Sender, string Text, ulong PartyId, Guid Id );

	/// <summary>
	/// A snapshot of a shared party. Unavailable also covers failed refreshes, so stale invites cannot be joined.
	/// </summary>
	internal record PartyInvitation( string Name, string GameTitle, int Members, int Capacity, bool IsPlaying, bool Available )
	{
		internal bool IsFull => Members >= Capacity;
	}

	readonly Dictionary<ulong, PartyInvitation> invitations = new();
	bool refreshingInvitations;
	double nextInvitationRefresh;

	readonly Dictionary<ulong, double> lastMessages = new();
	const string HistoryKey = "chat_history";
	const int HistoryLimit = 20;
	const int HistoryByteLimit = 8191;
	readonly List<Message> history = new();
	int generation;
	bool refreshing;
	double nextRefresh;
	double nextJoin;
	double lastSent = -10;
	double lastInvite = -10;

	sealed class Message
	{
		/// <summary>
		/// Identifies a message across live delivery and lobby history.
		/// </summary>
		public Guid Id { get; set; }

		/// <summary>
		/// Steam sender recorded by the receiver, never trusted from a live payload.
		/// </summary>
		public ulong SenderId { get; set; }

		/// <summary>
		/// Plain text sent to everyone in global chat.
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// Optional invitation to an existing party.
		/// </summary>
		public ulong PartyId { get; set; }
	}

	internal void Open()
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( Visible ) return;
		Visible = true;
		Tick();
	}

	internal void Close()
	{
		if ( !Visible && Channel is null ) return;
		Visible = false;
		generation++;
		cancellation.Cancel();
		cancellation.Dispose();
		cancellation = new();
		Channel?.Dispose();
		Channel = null;
		Connecting = false;
		refreshing = false;
		refreshingInvitations = false;
		// Leaving the page releases the channel, but keeps the conversation and draft for returning to it.
		invitations.Clear();
		lastMessages.Clear();
		history.Clear();
		Error = null;
		nextJoin = 0;
		nextRefresh = 0;
		nextInvitationRefresh = 0;
		Revision++;
	}

	internal void Tick()
	{
		if ( Channel is { IsJoined: false } )
		{
			Close();
			Open();
			return;
		}
		if ( !Visible ) return;
		if ( Channel is null )
		{
			if ( !Connecting && RealTime.Now >= nextJoin ) _ = Join();
			return;
		}
		if ( !Connecting && !refreshing && RealTime.Now >= nextRefresh ) _ = Refresh();
		if ( !refreshingInvitations && RealTime.Now >= nextInvitationRefresh ) _ = RefreshInvitations();
	}

	async Task Join()
	{
		Connecting = true;
		var version = generation;
		ChatChannel joined = null;
		try
		{
			var found = await bridge.FindAsync( cancellation.Token );
			if ( version != generation || !Visible ) return;
			foreach ( var channel in found.OrderBy( x => x ) )
			{
				try { joined = await bridge.JoinAsync( channel ); }
				catch ( InvalidOperationException ) { }
				if ( joined is not null || version != generation || !Visible ) break;
			}
			if ( version != generation || !Visible ) return;
			joined ??= await bridge.CreateAsync();
			if ( version != generation || !Visible ) return;
			Attach( joined );
			joined = null;
			Error = null;
		}
		catch ( Exception e )
		{
			if ( version == generation && Visible )
			{
				Error = e.Message;
				nextJoin = RealTime.Now + 10;
			}
		}
		finally
		{
			joined?.Dispose();
			if ( version == generation ) { Connecting = false; Revision++; }
		}
	}

	void Attach( ChatChannel channel )
	{
		Channel?.Dispose();
		Channel = channel;
		// Rejoining or merging channels must not erase messages already received in this session.
		invitations.Clear();
		nextInvitationRefresh = 0;
		lastMessages.Clear();
		history.Clear();
		LoadHistory();
		channel.MessageReceived += Receive;
		channel.Changed += Changed;
		Changed();
	}

	/// <summary>
	/// All messages sharing the same party use one periodically refreshed snapshot.
	/// </summary>
	internal PartyInvitation GetInvitation( ulong id ) => invitations.GetValueOrDefault( id );

	async Task RefreshInvitations()
	{
		using var scope = EnterMenuScope();
		refreshingInvitations = true;
		nextInvitationRefresh = RealTime.Now + 15;
		var version = generation;
		var channel = Channel;

		try
		{
			var ids = Messages.Where( x => x.PartyId != 0 ).Select( x => x.PartyId ).Distinct().ToArray();
			foreach ( var id in invitations.Keys.Except( ids ).ToArray() ) invitations.Remove( id );

			// Bound concurrent Steam requests, including when chat contains many different invitations.
			foreach ( var batch in ids.Chunk( 8 ) )
			{
				if ( version != generation || !Visible || Channel != channel ) return;

				await Task.WhenAll( batch.Select( async id =>
				{
					PartyRoom.Entry? entry = null;
					try
					{
						entry = await PartyRoom.GetEntryAsync( id );
					}
					catch ( Exception )
					{
						// A failed check must not leave an old invitation looking live.
					}

					if ( version != generation || !Visible || Channel != channel ) return;

					invitations[id] = entry is { } party
						? new( party.Name, party.GameTitle, party.Members, party.MaxMembers, party.IsPlaying, true )
						: new( invitations.GetValueOrDefault( id )?.Name, invitations.GetValueOrDefault( id )?.GameTitle, 0, 0, false, false );
					InvitationRevision++;
				} ) );
			}
		}
		finally
		{
			if ( version == generation ) refreshingInvitations = false;
		}
	}

	async Task Refresh()
	{
		refreshing = true;
		nextRefresh = RealTime.Now + 15;
		var version = generation;
		try
		{
			// Merge channels created at the same time once Steam lists both of them.
			var current = Channel;
			if ( current is null || Connecting ) return;
			var found = await bridge.FindAsync( cancellation.Token );
			if ( version != generation || !Visible || Channel != current || Connecting ) return;
			Error = null;
			var older = found.Where( x => x < current.Id ).OrderBy( x => x ).FirstOrDefault();
			if ( older == 0 ) return;
			Connecting = true;
			try
			{
				var joined = await bridge.JoinAsync( older );
				if ( version != generation || !Visible )
				{
					joined.Dispose();
				}
				else
				{
					Attach( joined );
				}
			}
			finally { if ( version == generation ) Connecting = false; }
		}
		catch ( Exception e )
		{
			if ( version == generation && Visible ) Error = e.Message;
		}
		finally { if ( version == generation ) { refreshing = false; Revision++; } }
	}

	internal void InviteParty()
	{
		if ( !CanInviteParty || PartyRoom.Current is not { } party ) return;

		try
		{
			party.MakePublic();
			if ( SendMessage( "", party.Id.ValueUnsigned ) ) lastInvite = RealTime.Now;
			nextRefresh = 0;
		}
		catch ( Exception e ) { Error = e.Message; }
	}

	internal bool Send( string text ) => SendMessage( text, 0 );

	bool SendMessage( string text, ulong partyId )
	{
		text = text?.Trim() ?? "";
		if ( !CanSend || (text.Length == 0 && partyId == 0) ) return false;

		if ( text.Length > 400 ) text = text[..400];
		if ( !Channel.Send( JsonSerializer.Serialize( new Message { Id = Guid.NewGuid(), Text = text, PartyId = partyId } ) ) )
		{
			Error = "The message could not be sent.";
			return false;
		}
		Error = null;
		lastSent = RealTime.Now;
		return true;
	}

	void Receive( Friend sender, string payload )
	{
		if ( sender.IsBlocked || !Visible ) return;
		if ( lastMessages.TryGetValue( sender.Id, out var last ) && RealTime.Now - last < 0.5 ) return;
		try
		{
			var message = JsonSerializer.Deserialize<Message>( payload );
			if ( !IsValid( message ) ) return;
			lastMessages[sender.Id] = RealTime.Now;

			// Older clients do not include a message ID.
			if ( message.Id == Guid.Empty ) message.Id = Guid.NewGuid();
			message.SenderId = sender.Id;
			Remember( message );
			AddMessage( message, true );
			SaveHistory();
		}
		catch ( JsonException ) { }
	}

	bool IsValid( Message message ) => message is not null && message.Text?.Length is not > 400 && (message.PartyId != 0 || !string.IsNullOrWhiteSpace( message.Text ));

	void Remember( Message message )
	{
		if ( history.Any( x => x.Id == message.Id && x.SenderId == message.SenderId ) ) return;

		history.Add( message );
		if ( history.Count > HistoryLimit ) history.RemoveAt( 0 );
	}

	void AddMessage( Message message, bool live = false )
	{
		var sender = new Friend( message.SenderId );
		if ( sender.IsBlocked || Messages.Any( x => x.Id == message.Id && x.Sender.Id == message.SenderId ) ) return;

		var text = Sandbox.Utility.Steam.FilterChat( message.Text, sender.Id );
		var line = new ChatLine( sender, text, message.PartyId, message.Id );
		Messages.Add( line );
		if ( message.PartyId != 0 && !invitations.ContainsKey( message.PartyId ) ) nextInvitationRefresh = 0;
		if ( Messages.Count > 100 ) Messages.RemoveAt( 0 );
		Revision++;
		if ( live )
		{
			try { LiveMessageReceived?.Invoke( line ); }
			catch ( Exception e ) { Log.Warning( e, "Global chat mention alert failed." ); }
		}
	}

	void LoadHistory()
	{
		var payload = Channel.GetData( HistoryKey );
		if ( string.IsNullOrEmpty( payload ) || Encoding.UTF8.GetByteCount( payload ) > HistoryByteLimit ) return;

		try
		{
			var messages = JsonSerializer.Deserialize<List<Message>>( payload );
			if ( messages is null ) return;

			foreach ( var message in messages.TakeLast( HistoryLimit ) )
			{
				if ( !IsValid( message ) || message.Id == Guid.Empty || message.SenderId == 0 ) continue;

				Remember( message );
				AddMessage( message );
			}
		}
		catch ( JsonException ) { }
	}

	void SaveHistory()
	{
		if ( Channel is null || !Channel.Owner.IsMe ) return;

		// Steam limits each metadata value to 8 KB, including its terminating byte.
		var payload = JsonSerializer.Serialize( history );
		while ( Encoding.UTF8.GetByteCount( payload ) > HistoryByteLimit && history.Count > 0 )
		{
			history.RemoveAt( 0 );
			payload = JsonSerializer.Serialize( history );
		}

		try
		{
			Channel.SetData( HistoryKey, payload );
		}
		catch ( InvalidOperationException e )
		{
			Error = e.Message;
			Revision++;
		}
	}

	void Changed()
	{
		// Every member keeps recent messages so a new owner can continue saving history.
		SaveHistory();
		Revision++;
	}

	public void Dispose()
	{
		if ( disposed ) return;
		Close();
		disposed = true;
		cancellation.Dispose();
		LiveMessageReceived = null;
	}
}
