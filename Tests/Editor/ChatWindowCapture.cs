using Sandbox;
using Sandbox.UI;
using System;
using System.Linq;
using System.Reflection;

namespace Editor.GlobalChat;

/// <summary>Captures the chat panel through the editor window camera.</summary>
internal static class ChatWindowCapture
{
	internal static Bitmap Capture( PanelWindow window, GlobalChatSession session )
	{
		using var scope = session.EnterMenuScope();
		EngineChatBridge.Invoke( EngineChatBridge.RequireMethod( typeof( PanelWindow ), "Frame" ), window );
		var camera = typeof( PanelWindow ).GetField( "_camera", BindingFlags.Instance | BindingFlags.NonPublic )?.GetValue( window ) as SceneCamera
			?? throw new InvalidOperationException( "The engine does not expose the chat window camera." );
		var render = typeof( SceneCamera ).GetMethods( BindingFlags.Instance | BindingFlags.NonPublic )
			.Single( x => x.Name == "RenderToBitmap" && x.GetParameters().Length == 2 );
		var config = Activator.CreateInstance( render.GetParameters()[1].ParameterType.GetElementType() );
		var size = (Vector2)EngineChatBridge.RequireProperty( typeof( PanelWindow ), "PixelSize" ).GetValue( window );
		var bitmap = new Bitmap( (int)size.x, (int)size.y );
		try { EngineChatBridge.Invoke( render, camera, bitmap, config ); return bitmap; }
		catch { bitmap.Dispose(); throw; }
	}

}
