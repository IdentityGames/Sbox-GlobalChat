using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Editor.GlobalChat;

/// <summary>Plays mention alerts outside the game audio mixer, including when the editor is in the background.</summary>
internal static class ChatMentionSound
{
	// Use the longer editor chime: the compile-success tick is easy to miss.
	internal const string SoundFile = "core/sounds/editor/published.wav";
	const uint Async = 0x0001;
	const uint NoDefault = 0x0002;
	const uint FileName = 0x00020000;

	internal static bool Play( out string error )
	{
		error = null;
		if ( !OperatingSystem.IsWindows() )
		{
			error = "Editor chat sound requires Windows.";
			return false;
		}
		var path = FileSystem.Root.GetFullPath( SoundFile );
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) )
		{
			error = $"Editor chat sound file is missing: {SoundFile}";
			return false;
		}
		// EditorUtility.PlayRawSound only confirms path resolution. Call Windows directly
		// so a failed playback request is reported, rather than counted as a played sound.
		if ( PlaySound( path, IntPtr.Zero, Async | NoDefault | FileName ) ) return true;
		error = "Windows could not start the editor chat sound. Check the audio output device.";
		return false;
	}

	[DllImport( "winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, ExactSpelling = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool PlaySound( string file, IntPtr module, uint flags );
}
