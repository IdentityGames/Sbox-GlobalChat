using Sandbox;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Editor.GlobalChat;

/// <summary>Embeds the existing SDL surface in a native editor widget on Windows.</summary>
internal sealed class ChatDockNativeHost : IDisposable, IHotloadManaged
{
	const int StyleIndex = -16;
	const long ChildStyle = 0x40000000;
	const long PopupStyle = 0x80000000;
	const long CaptionStyle = 0x00C00000;
	const long ThickFrameStyle = 0x00040000;
	readonly IntPtr child;
	readonly IntPtr parent;
	readonly Widget widget;
	readonly IntPtr sdlHandle;
	readonly MethodInfo mouseMove;
	readonly MethodInfo mouseButton;
	readonly object[] buttons;
	readonly GlobalChatWindow chat;
	readonly MethodInfo frame;
	readonly ChatDockKeyboardInput keyboard;
	[SkipHotload] IntPtr callbackRoot;
	bool inputHookInstalled;
	bool positioned;

	internal ChatDockNativeHost( GlobalChatWindow window, Widget widget )
	{
		this.widget = widget;
		chat = window;
		frame = EngineChatBridge.RequireMethod( typeof( PanelWindow ), "Frame" );
		if ( !OperatingSystem.IsWindows() ) throw new PlatformNotSupportedException( "Docked Global Chat currently requires the Windows editor." );
		var nativeWidget = typeof( Widget ).GetField( "_widget", BindingFlags.Instance | BindingFlags.NonPublic )?.GetValue( widget )
			?? throw new InvalidOperationException( "The editor does not expose the native dock widget." );
		parent = (IntPtr)EngineChatBridge.Invoke( EngineChatBridge.RequireMethod( nativeWidget.GetType(), "winId" ), nativeWidget );
		var sdl = EngineChatBridge.RequireType( typeof( Friend ).Assembly, "NativeEngine.Sdl" );
		var handle = EngineChatBridge.RequireProperty( typeof( PanelWindow ), "Handle" ).GetValue( window );
		sdlHandle = (IntPtr)handle;
		var input = EngineChatBridge.RequireType( typeof( Friend ).Assembly, "Sandbox.UI.PanelWindowInput" );
		mouseMove = EngineChatBridge.RequireMethod( input, "OnMouseMove", typeof( IntPtr ), typeof( float ), typeof( float ), typeof( float ), typeof( float ) );
		var buttonType = EngineChatBridge.RequireType( typeof( Friend ).Assembly, "NativeEngine.ButtonCode" );
		mouseButton = EngineChatBridge.RequireMethod( input, "OnMouseButton", typeof( IntPtr ), buttonType, typeof( bool ), typeof( int ), typeof( KeyboardModifiers ) );
		buttons = [Enum.Parse( buttonType, "MouseLeft" ), Enum.Parse( buttonType, "MouseRight" ), Enum.Parse( buttonType, "MouseMiddle" )];
		var properties = EngineChatBridge.Invoke( EngineChatBridge.RequireMethod( sdl, "GetWindowProperties", typeof( IntPtr ) ), null, handle );
		child = (IntPtr)EngineChatBridge.Invoke( EngineChatBridge.RequireMethod( sdl, "GetPointerProperty", typeof( uint ), typeof( string ), typeof( IntPtr ) ), null, properties, "SDL.window.win32.hwnd", IntPtr.Zero );
		if ( parent == IntPtr.Zero || child == IntPtr.Zero ) throw new InvalidOperationException( "The editor did not create both native chat handles." );
		// SetParent does not change window styles. Set WS_CHILD and clear WS_POPUP first.
		var style = GetWindowLongPtr( child, StyleIndex ).ToInt64();
		Marshal.SetLastPInvokeError( 0 );
		SetWindowLongPtr( child, StyleIndex, new IntPtr( (style | ChildStyle) & ~(PopupStyle | CaptionStyle | ThickFrameStyle) ) );
		if ( Marshal.GetLastPInvokeError() != 0 ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		Marshal.SetLastPInvokeError( 0 );
		SetParent( child, parent );
		if ( GetParent( child ) != parent ) throw new Win32Exception( Marshal.GetLastPInvokeError(), "Could not attach chat to its editor dock." );
		UpdateBounds();
		// An SDL top-level window relies on activation for keyboard focus. After it
		// becomes a Qt child, mouse activation belongs to the editor's top-level window.
		InstallInputHook();
		try { keyboard = new ChatDockKeyboardInput( child, sdlHandle, window ); }
		catch { Dispose(); throw; }
	}

	void InstallInputHook()
	{
		SubclassProc inputCallback = HandleNativeInput;
		callbackRoot = GCHandle.ToIntPtr( GCHandle.Alloc( inputCallback ) );
		if ( !SetWindowSubclass( child, inputCallback, 1, UIntPtr.Zero ) )
		{
			GCHandle.FromIntPtr( callbackRoot ).Free();
			callbackRoot = IntPtr.Zero;
			throw new InvalidOperationException( "Could not install input handling for the chat dock." );
		}
		inputHookInstalled = true;
	}

	void IHotloadManaged.Destroyed( Dictionary<string, object> state )
	{
		state["InputHookInstalled"] = inputHookInstalled;
		RemoveInputHook();
	}

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		inputHookInstalled = false;
		callbackRoot = IntPtr.Zero;
		if ( state.TryGetValue( "InputHookInstalled", out var installed ) && installed is true ) InstallInputHook();
	}

	void IHotloadManaged.Persisted()
	{
		if ( !inputHookInstalled ) return;
		RemoveInputHook();
		InstallInputHook();
	}

	void IHotloadManaged.Failed()
	{
		Dispose();
	}

	IntPtr HandleNativeInput( IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data )
	{
		// Focus only follows a deliberate click. The keyboard hook routes this
		// focused native child's keys before Qt can consume editor shortcuts.
		try
		{
			if ( message is 0x0007 or 0x0008 ) keyboard?.FocusChanged( message == 0x0007 );
			if ( message is >= 0x0200 and <= 0x0209 )
			{
				using var scope = chat.Session.EnterMenuScope();
				var coordinates = lParam.ToInt64();
				// SDL retains mouse tracking, leave events and wheel routing.
				if ( message == 0x0200 )
				{
					EngineChatBridge.Invoke( mouseMove, null, sdlHandle, (float)(short)coordinates, (float)(short)(coordinates >> 16), 0f, 0f );
					return DefSubclassProc( window, message, wParam, lParam );
				}
				var down = message is 0x0201 or 0x0203 or 0x0204 or 0x0206 or 0x0207 or 0x0209;
				if ( down ) { widget.Focus(); SetFocus( window ); SetCapture( window ); }
				else ReleaseCapture();
				// Qt focus briefly leaves the SDL child and clears its mouse state.
				// Restore the click position after focus, before dispatching the button.
				EngineChatBridge.Invoke( mouseMove, null, sdlHandle, (float)(short)coordinates, (float)(short)(coordinates >> 16), 0f, 0f );
				var button = buttons[message <= 0x0203 ? 0 : message <= 0x0206 ? 1 : 2];
				var modifiers = KeyboardModifiers.None;
				if ( (GetKeyState( 0x10 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Shift;
				if ( (GetKeyState( 0x11 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Ctrl;
				if ( (GetKeyState( 0x12 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Alt;
				EngineChatBridge.Invoke( mouseButton, null, sdlHandle, button, down, message is 0x0203 or 0x0206 or 0x0209 ? 2 : 1, modifiers );
				// Process each edge before Windows dispatches the next one. Otherwise a
				// press and release in the same frame can disappear from the UI state.
				EngineChatBridge.Invoke( frame, chat );
				return IntPtr.Zero;
			}
		}
		catch ( Exception e ) { Log.Error( e, "Global chat native mouse input failed." ); }
		if ( message == 0x0082 )
		{
			RemoveInputHook();
		}
		return DefSubclassProc( window, message, wParam, lParam );
	}

	public void Dispose()
	{
		keyboard?.Dispose();
		RemoveInputHook();
	}

	void RemoveInputHook()
	{
		if ( !inputHookInstalled ) return;
		var registeredCallback = (SubclassProc)GCHandle.FromIntPtr( callbackRoot ).Target;
		if ( !RemoveWindowSubclass( child, registeredCallback, 1 ) ) throw new InvalidOperationException( "Could not remove input handling for the chat dock." );
		inputHookInstalled = false;
		GCHandle.FromIntPtr( callbackRoot ).Free();
		callbackRoot = IntPtr.Zero;
	}

	internal bool IsAttached => GetParent( child ) == parent;
	internal IntPtr Handle => child;

	internal void UpdateBounds()
	{
		if ( !GetClientRect( parent, out var target ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		if ( !GetClientRect( child, out var current ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		if ( target.Right < 1 || target.Bottom < 1 || (positioned && target.Right == current.Right && target.Bottom == current.Bottom) ) return;
		// Native client sizes are already in device pixels. This also handles DPI changes.
		if ( !SetWindowPos( child, IntPtr.Zero, 0, 0, target.Right, target.Bottom, 0x0014 | 0x0020 ) ) throw new Win32Exception( Marshal.GetLastPInvokeError() );
		positioned = true;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct NativeRect { internal int Left, Top, Right, Bottom; }
	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	delegate IntPtr SubclassProc( IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data );
	[DllImport( "comctl32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool SetWindowSubclass( IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data );
	[DllImport( "comctl32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool RemoveWindowSubclass( IntPtr window, SubclassProc callback, UIntPtr id );
	[DllImport( "comctl32.dll" )]
	static extern IntPtr DefSubclassProc( IntPtr window, uint message, UIntPtr wParam, IntPtr lParam );
	[DllImport( "user32.dll" )]
	static extern IntPtr SetFocus( IntPtr window );
	[DllImport( "user32.dll" )]
	static extern IntPtr SetCapture( IntPtr window );
	[DllImport( "user32.dll" )]
	static extern bool ReleaseCapture();
	[DllImport( "user32.dll" )]
	static extern short GetKeyState( int key );
	[DllImport( "user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true )]
	static extern IntPtr GetWindowLongPtr( IntPtr window, int index );
	[DllImport( "user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true )]
	static extern IntPtr SetWindowLongPtr( IntPtr window, int index, IntPtr value );
	[DllImport( "user32.dll", SetLastError = true )]
	static extern IntPtr SetParent( IntPtr child, IntPtr parent );
	[DllImport( "user32.dll" )]
	static extern IntPtr GetParent( IntPtr child );
	[DllImport( "user32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool GetClientRect( IntPtr window, out NativeRect rect );
	[DllImport( "user32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool SetWindowPos( IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags );
}
