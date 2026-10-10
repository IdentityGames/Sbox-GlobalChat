using Sandbox;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Editor.GlobalChat;

/// <summary>Routes keys from the focused child before Qt applies editor shortcuts.</summary>
internal sealed class ChatDockKeyboardInput : IDisposable, IHotloadManaged
{
	const int GetMessageHook = 3;
	readonly IntPtr window;
	readonly IntPtr sdlHandle;
	readonly GlobalChatWindow chat;
	readonly MethodInfo translateKey;
	readonly MethodInfo keyInput;
	readonly MethodInfo textInput;
	readonly MethodInfo focusInput;
	readonly object surface;
	readonly object uiSystem;
	readonly MethodInfo tickInput;
	readonly PropertyInfo mouseInside;
	// Windows retains a function pointer, so the CLR must retain its delegate too.
	[SkipHotload] IntPtr callbackRoot;
	IntPtr hook;

	internal ChatDockKeyboardInput( IntPtr window, IntPtr sdlHandle, GlobalChatWindow chat )
	{
		this.window = window;
		this.sdlHandle = sdlHandle;
		this.chat = chat;
		var assembly = typeof( Friend ).Assembly;
		var input = EngineChatBridge.RequireType( assembly, "Sandbox.UI.PanelWindowInput" );
		var buttons = EngineChatBridge.RequireType( assembly, "NativeEngine.ButtonCode" );
		translateKey = EngineChatBridge.RequireMethod( EngineChatBridge.RequireType( assembly, "Sandbox.Engine.KeyTranslation" ), "VirtualKeyToButtonCode", typeof( int ) );
		keyInput = EngineChatBridge.RequireMethod( input, "OnKey", typeof( IntPtr ), buttons, typeof( bool ), typeof( KeyboardModifiers ) );
		textInput = EngineChatBridge.RequireMethod( input, "OnText", typeof( IntPtr ), typeof( string ) );
		focusInput = EngineChatBridge.RequireMethod( input, "OnFocus", typeof( IntPtr ), typeof( bool ) );
		surface = EngineChatBridge.RequireProperty( typeof( PanelWindow ), "Surface" ).GetValue( chat );
		uiSystem = EngineChatBridge.RequireProperty( surface.GetType(), "System" ).GetValue( surface );
		tickInput = EngineChatBridge.RequireMethod( uiSystem.GetType(), "TickSurfaceInput", typeof( bool ) );
		mouseInside = EngineChatBridge.RequireProperty( surface.GetType(), "MouseInside" );
		Install();
	}

	internal void FocusChanged( bool focused )
	{
		using var scope = chat.Session.EnterMenuScope();
		EngineChatBridge.Invoke( focusInput, null, sdlHandle, focused );
	}

	void Install()
	{
		MessageHook callback = OnMessage;
		callbackRoot = GCHandle.ToIntPtr( GCHandle.Alloc( callback ) );
		hook = SetWindowsHookEx( GetMessageHook, callback, IntPtr.Zero, GetWindowThreadProcessId( window, out _ ) );
		if ( hook == IntPtr.Zero )
		{
			var error = Marshal.GetLastPInvokeError();
			GCHandle.FromIntPtr( callbackRoot ).Free();
			callbackRoot = IntPtr.Zero;
			throw new Win32Exception( error, "Could not install chat keyboard input." );
		}
	}

	IntPtr OnMessage( int code, UIntPtr removed, IntPtr pointer )
	{
		// Observe only removed messages for this focused child. Other editor controls
		// and other applications retain their normal keyboard handling.
		if ( code >= 0 && removed == (UIntPtr)1 && GetFocus() == window )
		{
			try
			{
				var message = Marshal.PtrToStructure<NativeMessage>( pointer );
				if ( message.Window == window && message.Message is 0x0100 or 0x0101 or 0x0102 or 0x0104 or 0x0105 or 0x0106 )
				{
					using var scope = chat.Session.EnterMenuScope();
					if ( message.Message is 0x0102 or 0x0106 )
					{
						var character = (char)message.Key.ToUInt64();
						if ( !char.IsControl( character ) ) EngineChatBridge.Invoke( textInput, null, sdlHandle, character.ToString() );
					}
					else
					{
						var key = (int)message.Key.ToUInt64();
						var modifiers = KeyboardModifiers.None;
						if ( (GetKeyState( 0x10 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Shift;
						if ( (GetKeyState( 0x11 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Ctrl;
						if ( (GetKeyState( 0x12 ) & 0x8000) != 0 ) modifiers |= KeyboardModifiers.Alt;
						// Leave the operating system's close command intact.
						if ( key == 0x73 && modifiers.HasFlag( KeyboardModifiers.Alt ) ) return CallNextHookEx( hook, code, removed, pointer );
						var down = message.Message is 0x0100 or 0x0104;
						if ( down ) TranslateMessage( ref message );
						// VK_PACKET carries Unicode text, which arrives in WM_CHAR.
						if ( key != 0xE7 ) EngineChatBridge.Invoke( keyInput, null, sdlHandle, EngineChatBridge.Invoke( translateKey, null, key ), down, modifiers );
					}
					// Preserve native event order for arrows, selection and typing in one
					// frame. Use the engine's input tick without another render or layout.
					EngineChatBridge.Invoke( tickInput, uiSystem, (bool)mouseInside.GetValue( surface ) );
					message.Message = 0; // WM_NULL prevents duplicate SDL or Qt delivery.
					Marshal.StructureToPtr( message, pointer, false );
				}
			}
			catch ( Exception e ) { Log.Error( e, "Global chat native keyboard input failed." ); }
		}
		return CallNextHookEx( hook, code, removed, pointer );
	}

	public void Dispose()
	{
		if ( hook == IntPtr.Zero ) return;
		if ( !UnhookWindowsHookEx( hook ) ) throw new Win32Exception( Marshal.GetLastPInvokeError(), "Could not remove chat keyboard input." );
		hook = IntPtr.Zero;
		GCHandle.FromIntPtr( callbackRoot ).Free();
		callbackRoot = IntPtr.Zero;
	}

	void IHotloadManaged.Destroyed( Dictionary<string, object> state )
	{
		state["Installed"] = hook != IntPtr.Zero;
		Dispose();
	}

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		// Fields were copied before Destroyed removed the old native registration.
		hook = IntPtr.Zero;
		callbackRoot = IntPtr.Zero;
		if ( state.TryGetValue( "Installed", out var installed ) && installed is true ) Install();
	}

	void IHotloadManaged.Persisted()
	{
		if ( hook == IntPtr.Zero ) return;
		Dispose();
		Install();
	}

	void IHotloadManaged.Failed()
	{
		Dispose();
	}

	[StructLayout( LayoutKind.Sequential )]
	struct NativeMessage
	{
		internal IntPtr Window;
		internal uint Message;
		internal UIntPtr Key;
		internal IntPtr Parameter;
		internal uint Time;
		internal int X, Y;
		internal uint Private;
	}
	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	delegate IntPtr MessageHook( int code, UIntPtr removed, IntPtr pointer );
	[DllImport( "user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true )]
	static extern IntPtr SetWindowsHookEx( int type, MessageHook callback, IntPtr module, uint thread );
	[DllImport( "user32.dll", SetLastError = true )] static extern bool UnhookWindowsHookEx( IntPtr hook );
	[DllImport( "user32.dll" )] static extern IntPtr CallNextHookEx( IntPtr hook, int code, UIntPtr removed, IntPtr pointer );
	[DllImport( "user32.dll" )] static extern uint GetWindowThreadProcessId( IntPtr window, out uint process );
	[DllImport( "user32.dll" )] static extern IntPtr GetFocus();
	[DllImport( "user32.dll" )] static extern short GetKeyState( int key );
	[DllImport( "user32.dll" )] static extern bool TranslateMessage( ref NativeMessage message );
}
