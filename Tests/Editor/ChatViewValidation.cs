using Sandbox;
using Sandbox.UI;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Editor.GlobalChat;

/// <summary>Local checks through Windows input. Never activates Send or a party action.</summary>
internal static class ChatViewValidation
{
	internal static async Task<object> Run( GlobalChatWindow window )
	{
		using var scope = window.Session.EnterMenuScope();
		var popups = GlobalChatWindow.MentionPopups;
		var sounds = GlobalChatWindow.MentionSounds;
		var draft = window.Session.Draft;
		var frame = EngineChatBridge.RequireMethod( typeof( PanelWindow ), "Frame" );
		window.View.DismissUserActions();
		EngineChatBridge.Invoke( frame, window );
		var input = (TextEntry)Find( window.Root, "chat-input" );
		var settingsWereOpen = Find( window.Root, "chat-popups" ) is not null;
		var previewMessages = window.View.PreviewMessages;
		var membersWereVisible = window.View.MembersVisible;
		try
		{
			window.View.MembersVisible = true;
			EngineChatBridge.Invoke( frame, window );
			await Task.Delay( 150 );
			var heading = Find( window.Root, "chat-conversation-heading" );
			var shownWidth = heading.Box.Rect.Width;
			await Click( window, Find( window.Root, "chat-members-toggle" ), frame );
			if ( window.View.MembersVisible || Find( window.Root, "chat-member-list" ) is not null || heading.Box.Rect.Width <= shownWidth )
				throw new InvalidOperationException( $"The user list button did not hide the list and expand chat. Visible: {window.View.MembersVisible}; list present: {Find( window.Root, "chat-member-list" ) is not null}; width: {heading.Box.Rect.Width}; prior width: {shownWidth}; mouse: {window.MousePosition}; target: {Find( window.Root, "chat-members-toggle" ).Box.Rect}." );
			await Click( window, Find( window.Root, "chat-members-toggle" ), frame );
			if ( !window.View.MembersVisible || Find( window.Root, "chat-member-list" ) is null )
				throw new InvalidOperationException( "The user list button did not show the list." );
			if ( !settingsWereOpen ) await Click( window, Find( window.Root, "chat-settings-toggle" ), frame );
			await Click( window, Find( window.Root, "chat-popups" ), frame );
			if ( GlobalChatWindow.MentionPopups == popups || GlobalChatWindow.MentionSounds != sounds ) throw new InvalidOperationException( "Popup checkbox did not change independently." );
			await Click( window, Find( window.Root, "chat-sounds" ), frame );
			if ( GlobalChatWindow.MentionSounds == sounds ) throw new InvalidOperationException( "Sound checkbox did not change." );
			input.Text = "";
			window.Session.Draft = "";
			await Click( window, input, frame );
			ChatNativeInputValidation.TypeUnicode( "Local input check" );
			EngineChatBridge.Invoke( frame, window );
			await Task.Delay( 100 );
			if ( input.Text != "Local input check" || window.Session.Draft != input.Text ) throw new InvalidOperationException( "Typing did not reach the Razor draft binding." );
			var me = new Friend( Game.SteamId );
			await Click( window, Find( window.Root, $"chat-member-{me.Id}" ), frame );
			var menu = Find( window.Root, "chat-user-actions" );
			if ( menu is null || Find( menu, "chat-user-steam" ) is null || Find( menu, "chat-user-website" ) is null )
				throw new InvalidOperationException( "The user menu does not contain both profile actions." );
			var bounds = window.View.Box.Rect;
			if ( menu.Box.Rect.Left < bounds.Left || menu.Box.Rect.Right > bounds.Right || menu.Box.Rect.Top < bounds.Top || menu.Box.Rect.Bottom > bounds.Bottom )
				throw new InvalidOperationException( "The user menu extends outside chat." );
			await Click( window, Find( menu, "chat-user-mention" ), frame );
			if ( input.Text != $"Local input check @{me.Name} " || window.Session.Draft != input.Text || Find( window.Root, "chat-user-actions" ) is not null )
				throw new InvalidOperationException( "The mention action did not update the draft and close the menu." );
			await Click( window, Find( window.Root, $"chat-member-{me.Id}" ), frame );
			await Click( window, Find( window.Root, "chat-user-dismiss" ), frame );
			if ( Find( window.Root, "chat-user-actions" ) is not null ) throw new InvalidOperationException( "Clicking outside did not close the user menu." );
			var message = new GlobalChatSession.ChatLine( me, "Local user action check", 0, Guid.NewGuid() );
			window.View.PreviewMessages = [message];
			EngineChatBridge.Invoke( frame, window );
			await Click( window, Find( window.Root, $"chat-name-{message.Id}" ), frame );
			if ( Find( window.Root, "chat-user-actions" ) is null ) throw new InvalidOperationException( "Clicking a message name did not open the menu." );
			window.View.DismissUserActions();
			input.Text = new string( 'x', 400 );
			window.Session.Draft = input.Text;
			EngineChatBridge.Invoke( frame, window );
			await Task.Delay( 100 );
			EngineChatBridge.Invoke( frame, window );
			await Click( window, Find( window.Root, $"chat-name-{message.Id}" ), frame );
			var mentionButton = Find( window.Root, "chat-user-mention" )
				?? throw new InvalidOperationException( "The message name did not reopen the user menu." );
			if ( !mentionButton.HasClass( "disabled" ) || input.Text.Length != 400 )
				throw new InvalidOperationException( "The full draft does not disable mention insertion." );
			ChatNativeInputValidation.PressKey( 0x1B );
			EngineChatBridge.Invoke( frame, window );
			await Task.Delay( 100 );
			EngineChatBridge.Invoke( frame, window );
			if ( Find( window.Root, "chat-user-actions" ) is not null ) throw new InvalidOperationException( "Escape did not close the user menu." );
			var font = input.ComputedStyle.FontFamily;
			return new { MemberListToggle = true, PopupToggle = true, SoundToggle = true, TypedDraft = true, UserMenu = true, MenuInBounds = true, MessageNameClick = true, MentionDraft = true, FullDraftPreserved = true, OutsideDismiss = true, EscapeDismiss = true, Font = font, SentMessages = 0, OpenedExternalProfiles = 0 };
		}
		finally
		{
			window.View.DismissUserActions();
			window.View.PreviewMessages = previewMessages;
			window.View.MembersVisible = membersWereVisible;
			GlobalChatWindow.MentionPopups = popups;
			GlobalChatWindow.MentionSounds = sounds;
			input.Text = draft;
			window.Session.Draft = draft;
			EditorCookie.Set( "GlobalChat.Draft", draft );
			EngineChatBridge.Invoke( frame, window );
			if ( !settingsWereOpen && Find( window.Root, "chat-popups" ) is not null ) await Click( window, Find( window.Root, "chat-settings-toggle" ), frame );
		}
	}

	internal static Panel Find( Panel root, string id )
	{
		if ( root.Id == id ) return root;
		foreach ( var child in root.Children )
		{
			var found = Find( child, id );
			if ( found is not null ) return found;
		}
		return null;
	}

	static async Task Click( GlobalChatWindow window, Panel target, MethodInfo frame )
	{
		await ChatNativeInputValidation.Click( window, target );
		EngineChatBridge.Invoke( frame, window );
	}
}
