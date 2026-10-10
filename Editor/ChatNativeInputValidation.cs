using Sandbox;
using Sandbox.UI;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Editor.GlobalChat;

/// <summary>Checks the Windows mouse, focus, keyboard and SDL route without sending chat.</summary>
internal static class ChatNativeInputValidation
{
	internal static async Task<object> Run( GlobalChatWindow window )
	{
		if ( !OperatingSystem.IsWindows() ) throw new PlatformNotSupportedException( "Native chat input validation requires Windows." );
		if ( (GetAsyncKeyState( 0x10 ) & 0x8000) != 0 || (GetAsyncKeyState( 0x11 ) & 0x8000) != 0 || (GetAsyncKeyState( 0x12 ) & 0x8000) != 0 )
			throw new InvalidOperationException( "Release Shift, Ctrl and Alt before checking native chat input." );
		using var scope = window.Session.EnterMenuScope();
		var host = (GlobalChatDock)GlobalChatDock.Dock.Widget;
		var child = host.NativeHandle;
		var input = (TextEntry)ChatViewValidation.Find( window.Root, "chat-input" );
		var draft = window.Session.Draft;
		var previousFocus = GetFocus();
		if ( !GetCursorPos( out var cursor ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		try
		{
			window.View.DismissUserActions();
			input.Text = "";
			window.Session.Draft = "";
			await Task.Delay( 150 );
			var root = GetAncestor( child, 2 );
			host.GetWindow().Raise();
			host.Focus();
			SetForegroundWindow( root );
			SetFocus( root );
			var point = new NativePoint { X = (int)input.Box.Rect.Center.x, Y = (int)input.Box.Rect.Center.y };
			if ( !ClientToScreen( child, ref point ) || !SetCursorPos( point.X, point.Y ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
			var underCursor = WindowFromPoint( point );
			GetWindowRect( child, out var nativeRect );
			GetCursorPos( out var actualCursor );
			Send( [new() { Type = 0, Data = new() { Mouse = new() { Flags = 0x0002 } } }, new() { Type = 0, Data = new() { Mouse = new() { Flags = 0x0004 } } }] );
			await Task.Delay( 250 );
			if ( GetFocus() != child || !input.HasFocus ) throw new InvalidOperationException( $"Clicking the message field did not focus chat. Windows focus: {GetFocus() == child}; field focus: {input.HasFocus}; hit chat window: {underCursor == child}; target: {point.X},{point.Y}; cursor: {actualCursor.X},{actualCursor.Y}; window: {nativeRect.Left},{nativeRect.Top},{nativeRect.Right},{nativeRect.Bottom}; Qt: {host.ScreenPosition}; visible: {IsWindowVisible( child )}; root: {root}; hit: {underCursor}." );
			const string sample = "Native keyboard check ✓ 😊 ~ `";
			TypeUnicode( sample );
			await Task.Delay( 300 );
			if ( input.Text != sample || window.Session.Draft != sample ) throw new InvalidOperationException( $"Windows typing did not reach chat: expected '{sample}', received '{input.Text}'. Field focus: {input.HasFocus}; dock focus: {host.IsFocused}; editor focus: {Application.FocusWidget?.GetType().FullName}." );
			PressKey( 0x08 );
			await Task.Delay( 200 );
			if ( input.Text != sample[..^1] || window.Session.Draft != input.Text ) throw new InvalidOperationException( "Windows Backspace did not edit the chat draft." );
			PressKey( 0x24 ); // Home followed immediately by text must retain event order.
			TypeUnicode( "#@" );
			await Task.Delay( 150 );
			if ( input.Text != "#@" + sample[..^1] ) throw new InvalidOperationException( "Home followed by typing did not retain keyboard event order." );
			PressKey( 0x41, control: true );
			PressKey( 0x08 );
			await Task.Delay( 150 );
			if ( input.Text.Length != 0 || window.Session.Draft.Length != 0 ) throw new InvalidOperationException( "Ctrl+A and Backspace did not clear the draft." );
			// Test the current clipboard through the public paste policy. Do not replace
			// clipboard contents or expose them in the result.
			var clipboardText = EditorUtility.Clipboard.Paste();
			input.OnPaste( clipboardText );
			var pasted = input.Text;
			input.Text = "Paste selection check";
			window.Session.Draft = input.Text;
			// The engine does not invoke paste when the clipboard has no text.
			if ( string.IsNullOrEmpty( clipboardText ) ) pasted = input.Text;
			PressKey( 0x41, control: true );
			PressKey( 0x56, control: true );
			await Task.Delay( 150 );
			if ( input.Text != pasted || window.Session.Draft != pasted ) throw new InvalidOperationException( "Ctrl+V did not reach the chat paste policy." );
			SetFocus( root );
			TypeUnicode( "✓" );
			await Task.Delay( 150 );
			if ( input.Text != pasted ) throw new InvalidOperationException( "Chat consumed keys after losing native focus." );
			return new { NativeClickFocus = true, NativeUnicodeTyping = true, NativeBackspace = true, NativeHome = true, NativeSelectAll = true, NativePaste = true, FocusReleased = true, DraftBinding = true, SentMessages = 0 };
		}
		finally
		{
			input.Text = draft;
			window.Session.Draft = draft;
			EditorCookie.Set( "GlobalChat.Draft", draft );
			SetCursorPos( cursor.X, cursor.Y );
			if ( previousFocus != IntPtr.Zero ) SetFocus( previousFocus );
		}
	}

	internal static async Task Click( GlobalChatWindow window, Panel target )
	{
		if ( target is null ) throw new InvalidOperationException( "The expected Razor control is absent." );
		var host = (GlobalChatDock)GlobalChatDock.Dock.Widget;
		var child = host.NativeHandle;
		if ( !GetCursorPos( out var cursor ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		try
		{
			host.GetWindow().Raise();
			host.Focus();
			SetForegroundWindow( GetAncestor( child, 2 ) );
			await Task.Delay( 100 );
			var point = new NativePoint { X = (int)target.Box.Rect.Center.x, Y = (int)target.Box.Rect.Center.y };
			if ( !ClientToScreen( child, ref point ) || !SetCursorPos( point.X, point.Y ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
			await Task.Delay( 100 );
			if ( WindowFromPoint( point ) != child )
			{
				GetWindowRect( child, out var rect );
				throw new InvalidOperationException( $"The chat control is covered by another window. Target: {point.X},{point.Y}; child bounds: {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}; hit: {WindowFromPoint( point )}; child: {child}; root: {GetAncestor( child, 2 )}." );
			}
			Send( [new() { Type = 0, Data = new() { Mouse = new() { Flags = 0x0002 } } }] );
			await Task.Delay( 50 );
			Send( [new() { Type = 0, Data = new() { Mouse = new() { Flags = 0x0004 } } }] );
			await Task.Delay( 150 );
		}
		finally { SetCursorPos( cursor.X, cursor.Y ); }
	}

	internal static void TypeUnicode( string text )
	{
		var keys = new NativeInput[text.Length * 2];
		for ( var i = 0; i < text.Length; i++ )
		{
			keys[i * 2] = new() { Type = 1, Data = new() { Keyboard = new() { Scan = text[i], Flags = 0x0004 } } };
			keys[i * 2 + 1] = new() { Type = 1, Data = new() { Keyboard = new() { Scan = text[i], Flags = 0x0006 } } };
		}
		Send( keys );
	}

	internal static void PressKey( ushort key, bool control = false )
	{
		var press = new NativeInput { Type = 1, Data = new() { Keyboard = new() { Key = key } } };
		var release = new NativeInput { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = 0x0002 } } };
		if ( control ) Send( [new() { Type = 1, Data = new() { Keyboard = new() { Key = 0x11 } } }, press, release, new() { Type = 1, Data = new() { Keyboard = new() { Key = 0x11, Flags = 0x0002 } } }] );
		else Send( [press, release] );
	}

	static void Send( NativeInput[] events )
	{
		if ( SendInput( (uint)events.Length, events, Marshal.SizeOf<NativeInput>() ) != events.Length ) throw new Win32Exception( Marshal.GetLastPInvokeError(), "Windows did not accept the native input check." );
	}

	[StructLayout( LayoutKind.Sequential )]
	struct NativeInput { internal uint Type; internal InputData Data; }
	[StructLayout( LayoutKind.Explicit )]
	struct InputData { [FieldOffset( 0 )] internal MouseInput Mouse; [FieldOffset( 0 )] internal KeyboardInput Keyboard; }
	[StructLayout( LayoutKind.Sequential )]
	struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal UIntPtr Extra; }
	[StructLayout( LayoutKind.Sequential )]
	struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
	[StructLayout( LayoutKind.Sequential )]
	struct NativePoint { internal int X, Y; }
	[StructLayout( LayoutKind.Sequential )]
	struct NativeRect { internal int Left, Top, Right, Bottom; }
	[DllImport( "user32.dll", SetLastError = true )]
	static extern uint SendInput( uint count, NativeInput[] events, int size );
	[DllImport( "user32.dll" )] static extern IntPtr GetFocus();
	[DllImport( "user32.dll" )] static extern IntPtr WindowFromPoint( NativePoint point );
	[DllImport( "user32.dll" )] static extern bool GetWindowRect( IntPtr window, out NativeRect rect );
	[DllImport( "user32.dll" )] static extern bool IsWindowVisible( IntPtr window );
	[DllImport( "user32.dll" )] static extern IntPtr SetFocus( IntPtr window );
	[DllImport( "user32.dll" )] static extern IntPtr GetAncestor( IntPtr window, uint flags );
	[DllImport( "user32.dll" )] static extern bool SetForegroundWindow( IntPtr window );
	[DllImport( "user32.dll" )] static extern short GetAsyncKeyState( int key );
	[DllImport( "user32.dll", SetLastError = true )] static extern bool GetCursorPos( out NativePoint point );
	[DllImport( "user32.dll", SetLastError = true )] static extern bool SetCursorPos( int x, int y );
	[DllImport( "user32.dll", SetLastError = true )] static extern bool ClientToScreen( IntPtr window, ref NativePoint point );
}
