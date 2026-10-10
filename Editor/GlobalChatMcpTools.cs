using Sandbox;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.Mcp;

[McpToolset( "editor_global_chat", "Editor global chat connection and local validation. Never sends chat messages." )]
public static class GlobalChatMcpTools
{
	/// <summary>Checks a real Windows click, focus, Unicode typing, Backspace, Home, Ctrl+A, Ctrl+V and focus release. Uses existing clipboard text without changing the clipboard or returning its contents. Brings the editor forward, then restores draft, cursor and prior editor focus. Release modifiers first. Never presses Enter or sends chat. Use read_console afterwards.</summary>
	[McpTool( "editor_global_chat_validate_keyboard" )]
	public static async Task<object> ValidateKeyboard()
	{
		GlobalChat.GlobalChatWindow.Open();
		await Task.Delay( 150 );
		return await GlobalChat.ChatNativeInputValidation.Run( GlobalChat.GlobalChatWindow.Current );
	}
	/// <summary>Opens chat, then floats its editor dock or places it at the bottom. Keeps the same chat session and draft. Use editor_global_chat_status and editor_global_chat_capture next. Does not send a message.</summary>
	[McpTool( "editor_global_chat_dock" )]
	public static ChatStatus SetDock( bool floating = false )
	{
		GlobalChat.GlobalChatWindow.Open();
		var dock = GlobalChat.GlobalChatDock.Dock;
		if ( floating ) dock.Float();
		else EditorWindow.DockManager.OpenDock( "Global Chat", DockArea.Bottom );
		((GlobalChat.GlobalChatDock)dock.Widget).Synchronize();
		return Status();
	}

	/// <summary>Checks floating and docking with the same surface, connection and draft, then restores the editor layout in all cases. Requires joined chat. Forces C# memory collection to check native callback lifetime, then runs local Windows input checks in both states. Does not send chat or open profiles. Use read_console afterwards.</summary>
	[McpTool( "editor_global_chat_validate_docking" )]
	public static async Task<object> ValidateDocking()
	{
		GlobalChat.GlobalChatWindow.Open();
		var window = GlobalChat.GlobalChatWindow.Current;
		if ( window.Session.Channel is not { IsJoined: true } ) throw new InvalidOperationException( "Wait for global chat to join before checking docking." );
		var layout = EditorWindow.DockManager.State;
		var channel = window.Session.Channel.Id;
		var draft = window.Session.Draft;
		try
		{
			SetDock( true );
			// Keep the native input check on-screen, then restore the saved layout below.
			GlobalChat.GlobalChatDock.ResizeFloating( new Vector2( 720, 520 ) );
			var floatingWindow = ((GlobalChat.GlobalChatDock)GlobalChat.GlobalChatDock.Dock.Widget).GetWindow();
			floatingWindow.Position = floatingWindow.ScreenGeometry.Position + (floatingWindow.ScreenGeometry.Size - floatingWindow.Size) * 0.5f;
			await Task.Delay( 250 );
			var floating = Status();
			if ( !floating.Floating || !floating.SurfaceAttached ) throw new InvalidOperationException( "The chat surface did not stay attached when floating." );
			// Native callbacks must remain rooted even when the CLR collects delegates.
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			await GlobalChat.ChatViewValidation.Run( window );
			await GlobalChat.ChatNativeInputValidation.Run( window );
			SetDock( false );
			await Task.Delay( 250 );
			var docked = Status();
			if ( !docked.Docked || !docked.SurfaceAttached ) throw new InvalidOperationException( "The chat surface did not stay attached when docking." );
			await GlobalChat.ChatViewValidation.Run( window );
			await GlobalChat.ChatNativeInputValidation.Run( window );
			if ( GlobalChat.GlobalChatWindow.Current != window || window.Session.Channel?.Id != channel || window.Session.Draft != draft )
				throw new InvalidOperationException( "Docking changed the chat session or draft." );
			return new { Floating = true, Docked = true, SurfaceAttached = true, SameSession = true, DraftPreserved = true, InputInBothStates = true, SentMessages = 0 };
		}
		finally
		{
			if ( !EditorWindow.DockManager.RestoreState( layout ) ) throw new InvalidOperationException( "The editor could not restore its dock layout." );
			((GlobalChat.GlobalChatDock)GlobalChat.GlobalChatDock.Dock.Widget).Synchronize();
		}
	}

	/// <summary>Checks settings, draft typing, member menu, mention insertion and outside dismissal through window input. Restores draft and preferences. Requires joined chat. Never activates Send, external profiles or party actions. Use editor_global_chat_capture to review the layout next.</summary>
	[McpTool( "editor_global_chat_validate_view" )]
	public static async Task<object> ValidateView()
	{
		GlobalChat.GlobalChatWindow.Open();
		return await GlobalChat.ChatViewValidation.Run( GlobalChat.GlobalChatWindow.Current );
	}
	/// <summary>Captures the actual Razor chat surface. Optional size (540–1800 wide, 420–1400 high) requires a floating dock; omit both dimensions when docked. previewMessages and showSettings apply only to this capture. Does not change history or send chat. Open chat first; use read_console after capture.</summary>
	[McpTool( "editor_global_chat_capture" )]
	public static McpResult Capture( int width = 0, int height = 0, bool previewMessages = false, bool showSettings = false, bool? showMembers = null )
	{
		var window = GlobalChat.GlobalChatWindow.Current;
		if ( window is not { IsOpen: true } ) throw new InvalidOperationException( "Open Global Chat before capturing it." );
		if ( width != 0 || height != 0 )
		{
			if ( width < 540 || width > 1800 || height < 420 || height > 1400 ) throw new ArgumentOutOfRangeException( nameof( width ), "Use a width of 540–1800 and a height of 420–1400, or leave both zero." );
			GlobalChat.GlobalChatDock.ResizeFloating( new Vector2( width, height ) );
		}
		var settingsWereOpen = window.View.SettingsOpen;
		var membersWereVisible = window.View.MembersVisible;
		try
		{
			if ( showSettings ) window.View.SettingsOpen = true;
			if ( showMembers.HasValue ) window.View.MembersVisible = showMembers.Value;
			if ( previewMessages )
			{
				var me = new Friend( Game.SteamId );
				window.View.PreviewMessages = [
					new( me, "Local layout preview. This message is not sent to global chat.", 0, Guid.NewGuid() ),
					new( me, "A second sample checks the shared message style and text wrapping when the editor window is narrow. The avatar, name, and message must stay aligned.", 0, Guid.NewGuid() ) ];
			}
			using var bitmap = GlobalChat.ChatWindowCapture.Capture( window, window.Session );
			return McpResult.Image( bitmap );
		}
		finally { window.View.PreviewMessages = null; window.View.SettingsOpen = settingsWereOpen; window.View.MembersVisible = membersWereVisible; }
	}
	/// <summary>Creates a temporary private Steam party, joins it through the editor reflection path, verifies one local member, then leaves in all cases. Refuses when already in a party. Sends no chat message or invitation.</summary>
	[McpTool( "editor_global_chat_validate_private_party" )]
	public static async Task<object> ValidatePrivateParty()
	{
		var members = await new GlobalChat.EngineChatBridge().ValidatePrivatePartyAsync();
		return new { Joined = true, Members = members, Left = PartyRoom.Current is null };
	}
	/// <summary>Captures chat with the inline user action menu. Use editor_global_chat_preview_profile first. Does not open a browser or send a message.</summary>
	[McpTool( "editor_global_chat_capture_profile" )]
	public static McpResult CaptureProfile()
	{
		var window = GlobalChat.GlobalChatWindow.Current;
		if ( window is not { IsOpen: true } ) throw new InvalidOperationException( "Open Global Chat before capturing a profile." );
		using var bitmap = GlobalChat.ChatWindowCapture.Capture( window, window.Session );
		return McpResult.Image( bitmap );
	}

	/// <summary>Opens the inline user action menu for the local user. Does not open a browser, send a message, or change party state. Use editor_global_chat_capture_profile to review it.</summary>
	[McpTool( "editor_global_chat_preview_profile" )]
	public static ChatStatus PreviewProfile()
	{
		GlobalChat.GlobalChatWindow.Open();
		GlobalChat.GlobalChatWindow.Current.View.ShowUserActions( new Friend( Game.SteamId ) );
		return Status();
	}

	/// <summary>Opens the editor global chat window and connects to the normal menu channel. Does not send a message. Call editor_global_chat_status after connection.</summary>
	[McpTool( "editor_global_chat_open" )]
	public static ChatStatus Open()
	{
		GlobalChat.GlobalChatWindow.Open();
		return Status();
	}

	/// <summary>Returns dock/floating state, native surface attachment, connection, channel metadata, member count and alert settings. Contains no message text. Use editor_global_chat_open to connect.</summary>
	[McpTool.ReadOnly( "editor_global_chat_status" )]
	public static ChatStatus Status()
	{
		var window = GlobalChat.GlobalChatWindow.Current;
		var session = window is { IsOpen: true } ? window.Session : null;
		var channel = session?.Channel;
		var dock = GlobalChat.GlobalChatDock.Dock;
		return new()
		{
			Open = window is { IsOpen: true }, Connecting = session?.Connecting ?? false,
			Docked = dock is not null && !dock.IsClosed && !dock.IsFloating,
			Floating = dock is not null && !dock.IsClosed && dock.IsFloating,
			SurfaceAttached = dock?.Widget is GlobalChat.GlobalChatDock host && host.SurfaceAttached,
			Joined = channel?.IsJoined ?? false, ChannelId = channel?.Id.ToString(),
			Channel = channel?.GetData( "channel" ), LobbyType = channel?.GetData( "lobby_type" ),
			Dev = channel?.GetData( "dev" ), Api = channel?.GetData( "api" ), Protocol = channel?.GetData( "protocol" ),
			Members = channel?.MemberCount ?? 0, Messages = session?.Messages.Count ?? 0, Error = session?.Error,
			MentionPopups = GlobalChat.GlobalChatWindow.MentionPopups,
			MentionSounds = GlobalChat.GlobalChatWindow.MentionSounds,
			PopupsShown = window is { IsOpen: true } ? window.PopupCount : 0,
			SoundsPlayed = window is { IsOpen: true } ? window.SoundCount : 0,
			SoundError = window is { IsOpen: true } ? window.SoundError : null
		};
	}

	/// <summary>Changes saved editor mention preferences. Does not affect compiler notifications or send chat. Call editor_global_chat_status to read back.</summary>
	[McpTool( "editor_global_chat_alert_settings" )]
	public static ChatStatus AlertSettings( bool popups, bool sounds )
	{
		GlobalChat.GlobalChatWindow.MentionPopups = popups;
		GlobalChat.GlobalChatWindow.MentionSounds = sounds;
		return Status();
	}

	/// <summary>Exercises the real local mention notification path with one synthetic message. Does not send to Steam or insert into chat history. Opens chat first. Tests the current saved popup and sound settings.</summary>
	[McpTool( "editor_global_chat_preview_mention" )]
	public static ChatStatus PreviewMention()
	{
		GlobalChat.GlobalChatWindow.Open();
		var window = GlobalChat.GlobalChatWindow.Current;
		var me = new Friend( Game.SteamId );
		window.NotifyMention( new( new Friend( 76561197960265729 ), $"@{me.Name} Mention alert preview.", 0, Guid.NewGuid() ) );
		return Status();
	}

	/// <summary>Closes the chat window and leaves its channel. Does not send a message. Draft and alert preferences stay saved.</summary>
	[McpTool( "editor_global_chat_close" )]
	public static ChatStatus Close()
	{
		GlobalChat.GlobalChatWindow.Current?.Close();
		return Status();
	}
}

public sealed class ChatStatus
{
	public bool Open { get; set; }
	public bool Docked { get; set; }
	public bool Floating { get; set; }
	public bool SurfaceAttached { get; set; }
	public bool Connecting { get; set; }
	public bool Joined { get; set; }
	public string ChannelId { get; set; }
	public string Channel { get; set; }
	public string LobbyType { get; set; }
	public string Dev { get; set; }
	public string Api { get; set; }
	public string Protocol { get; set; }
	public int Members { get; set; }
	public int Messages { get; set; }
	public string Error { get; set; }
	public bool MentionPopups { get; set; }
	public bool MentionSounds { get; set; }
	public int PopupsShown { get; set; }
	/// <summary>Number of sound requests accepted by Windows. This does not verify speaker output.</summary>
	public int SoundsPlayed { get; set; }
	/// <summary>Failure from the last sound request, or null when Windows accepted playback. Acceptance does not verify speaker output.</summary>
	public string SoundError { get; set; }
}
