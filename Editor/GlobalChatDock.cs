using Sandbox;
using System;
using System.Linq;

namespace Editor.GlobalChat;

/// <summary>Places the Razor chat surface in the editor's normal dock layout.</summary>
[Dock( "Editor", "Global Chat", "forum", DockArea.Bottom )]
public sealed class GlobalChatDock : Widget
{
	internal GlobalChatWindow ChatWindow { get; private set; }
	ChatDockNativeHost nativeHost;
	internal bool SurfaceAttached => ChatWindow is { IsOpen: true } && nativeHost is not null && nativeHost.IsAttached;
	internal IntPtr NativeHandle => nativeHost?.Handle ?? IntPtr.Zero;
	internal static DockWidget Dock => EditorWindow.IsValid() && EditorWindow.DockManager.IsValid()
		? EditorWindow.DockManager.FindDockWidget( "Global Chat" ) : null;

	public GlobalChatDock( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 540, 420 );
		FocusMode = FocusMode.TabOrClick;
		OnPaintOverride = SuppressParentPaint;
	}

	// A named callback survives hotload when constructor fields change.
	static bool SuppressParentPaint()
	{
		return true;
	}

	internal static void Open()
	{
		// Restore the library registration if project startup cleared the dock types.
		if ( !EditorWindow.DockManager.DockTypes.Any( info => info.Title == "Global Chat" ) )
			EditorWindow.DockManager.RegisterDockType( new DockManager.DockInfo { Title = "Global Chat", Icon = "forum", Area = DockArea.Bottom, CreateAction = CreateDock } );
		EditorWindow.DockManager.SetDockState( "Global Chat", true );
		var dock = Dock ?? throw new InvalidOperationException( "The editor has not registered the Global Chat dock." );
		dock.MinimumSizeFromContent = true;
		dock.SetAsCurrentTab();
		var host = dock.Widget as GlobalChatDock ?? throw new InvalidOperationException( "Global Chat has an unexpected dock host." );
		host.EnsureWindow();
		host.Synchronize();
		host.Focus();
	}

	static Widget CreateDock()
	{
		return new GlobalChatDock( EditorWindow );
	}

	internal static void Hide( GlobalChatWindow window )
	{
		var dock = Dock;
		if ( dock is not null && dock.Widget is GlobalChatDock host && host.ChatWindow == window ) dock.ToggleView( false );
	}

	void EnsureWindow()
	{
		if ( ChatWindow is { IsOpen: true } ) return;
		ChatWindow = GlobalChatWindow.CreateDocked();
		try { nativeHost = new ChatDockNativeHost( ChatWindow, this ); }
		catch { ChatWindow.Close(); ChatWindow = null; throw; }
	}

	[EditorEvent.Frame]
	internal void Synchronize()
	{
		// Rebind existing docks as well as docks made after a code reload.
		OnPaintOverride = SuppressParentPaint;
		var dock = EditorWindow.DockManager.FindDockWidget( this );
		if ( dock is null || dock.IsClosed )
		{
			nativeHost?.Dispose();
			ChatWindow?.CloseSurface();
			ChatWindow = null;
			nativeHost = null;
			return;
		}
		dock.MinimumSizeFromContent = true;
		if ( !Visible ) { ChatWindow?.Hide(); return; }
		EnsureWindow();
		nativeHost.UpdateBounds();
		if ( !ChatWindow.IsVisible ) ChatWindow.Show();
	}

	internal static void ResizeFloating( Vector2 size )
	{
		var dock = Dock;
		if ( dock is null || !dock.IsFloating ) throw new InvalidOperationException( "Float Global Chat before requesting a capture size, or omit width and height to capture the dock area." );
		var host = (GlobalChatDock)dock.Widget;
		var floatingWindow = host.GetWindow();
		floatingWindow.Size += size - host.Size;
		host.Synchronize();
	}

	public override void OnDestroyed()
	{
		// The native widget is already gone. Release chat without querying its dock.
		nativeHost?.Dispose();
		ChatWindow?.CloseSurface();
		ChatWindow = null;
		nativeHost = null;
		base.OnDestroyed();
	}
}
