using Sandbox;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Editor.GlobalChat;

/// <summary>Hosts the menu-style Razor chat in an editor panel window.</summary>
public sealed class GlobalChatWindow : PanelWindow
{
	internal static GlobalChatWindow Current { get; private set; }
	static GlobalChatSession sharedSession;
	internal GlobalChatSession Session { get; }
	internal GlobalChatView View { get; }
	static readonly ChatMentionGate mentionGate = new();
	internal bool PartyBusy { get; private set; }
	internal string PartyError { get; private set; }
	bool released;
	internal int PopupCount { get; private set; }
	internal int SoundCount { get; private set; }
	internal string SoundError { get; private set; }

	public static bool MentionPopups
	{
		get => EditorCookie.Get( "GlobalChat.MentionPopups", true );
		set => EditorCookie.Set( "GlobalChat.MentionPopups", value );
	}
	public static bool MentionSounds
	{
		get => EditorCookie.Get( "GlobalChat.MentionSounds", true );
		set => EditorCookie.Set( "GlobalChat.MentionSounds", value );
	}

	[Menu( "Editor", "Chat/Global Chat", "forum" )]
	public static void Open()
	{
		GlobalChatDock.Open();
	}

	internal static GlobalChatWindow CreateDocked()
	{
		Current?.CloseSurface();
		return Current = new GlobalChatWindow();
	}

	public GlobalChatWindow() : base( "Global Chat", new Vector2( 920, 720 ), null, borderless: true )
	{
		try
		{
			MinSize = new Vector2( 540, 420 );
			ResizeBorder = 0;
			DropShadow = false;
			RoundedCorners = false;
			BackgroundColor = Color.Parse( "#171a21" ) ?? Color.Black;
			Session = sharedSession ??= new GlobalChatSession();
			Session.Draft = EditorCookie.Get( "GlobalChat.Draft", "" );
			using var scope = Session.EnterMenuScope();
			View = new GlobalChatView { Session = Session, Host = this, Parent = Root };
			Session.LiveMessageReceived += NotifyMention;
			OnCloseRequested = HandleCloseRequested;
			Session.Open();
		}
		catch { ReleaseWindow(); Dispose(); throw; }
	}

	bool HandleCloseRequested()
	{
		ReleaseWindow();
		return true;
	}

	[EditorEvent.Hotload]
	static void RebindCloseCallback()
	{
		if ( Current is { IsOpen: true } ) Current.OnCloseRequested = Current.HandleCloseRequested;
	}

	[EditorEvent.Frame]
	static void Frame()
	{
		if ( Current is null ) return;
		if ( !Current.IsOpen ) { Current.Close(); return; }
		using var scope = Current.Session.EnterMenuScope();
		Current.Session.Tick();
	}

	internal async Task JoinParty( ulong id )
	{
		if ( PartyBusy || Session.GetInvitation( id ) is not { Available: true, IsFull: false } ) return;
		PartyBusy = true;
		PartyError = null;
		try
		{
			using var scope = Session.EnterMenuScope();
			if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "Leave your current party before joining another." );
			await Session.JoinPartyAsync( id );
		}
		catch ( Exception e ) { PartyError = e.Message; }
		finally { PartyBusy = false; }
	}

	internal void NotifyMention( GlobalChatSession.ChatLine message )
	{
		var me = new Friend( Game.SteamId );
		if ( !mentionGate.ShouldNotify( message.Id, message.Sender.Id, me.Id, me.Name, message.Text, true, message.Sender.IsBlocked, MentionPopups || MentionSounds ) ) return;
		if ( MentionPopups )
		{
			var toast = new MentionToast { Title = "Mention in global chat", Subtitle = message.Sender.Name, Icon = "alternate_email", IsRunning = false };
			var body = new Widget() { FixedWidth = 300 };
			body.Layout = Layout.Column();
			body.Layout.Add( new Label( Escape( message.Text ) ) { WordWrap = true } );
			var open = body.Layout.Add( new Button( "Open chat", "forum" ) );
			open.Clicked = () => { Open(); Current.View.ScrollToLatest(); ToastManager.Dismiss( toast ); };
			toast.SetBodyWidget( body );
			ToastManager.Remove( toast, 8 );
			PopupCount++;
		}
		if ( MentionSounds )
		{
			if ( ChatMentionSound.Play( out var error ) ) SoundCount++;
			else Log.Warning( $"Global chat mention sound failed: {error}" );
			SoundError = error;
		}
	}

	internal static string Escape( string text ) => "<span>" + WebUtility.HtmlEncode( text ?? "" ).Replace( "\n", "<br>" ) + "</span>";

	void ReleaseWindow()
	{
		if ( released ) return;
		released = true;
		if ( Session is not null ) Session.LiveMessageReceived -= NotifyMention;
		if ( Current is not null && Current != this ) return;
		Session?.Close();
		if ( Current == this ) Current = null;
	}

	public void Close()
	{
		GlobalChatDock.Hide( this );
		CloseSurface();
	}

	internal void CloseSurface()
	{
		ReleaseWindow();
		Dispose();
	}

	[Event( "app.exit" )]
	static void Shutdown()
	{
		Current?.CloseSurface();
		sharedSession?.Dispose();
		sharedSession = null;
	}

	sealed class MentionToast : ToastWidget
	{
		public override bool WantsVisible => MentionPopups;
	}
}
