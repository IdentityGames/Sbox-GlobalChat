using Sandbox;

namespace Editor.Mcp;

[McpToolset( "editor_global_chat", "Editor global chat connection, docking and settings. Never sends chat messages." )]
public static partial class GlobalChatMcpTools
{
	/// <summary>Opens chat, then floats its editor dock or places it at the bottom. Keeps the same chat session and draft. Use editor_global_chat_status next. Does not send a message.</summary>
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
