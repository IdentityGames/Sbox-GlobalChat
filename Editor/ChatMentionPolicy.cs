using System;
using System.Collections.Generic;
using System.Globalization;

namespace Editor.GlobalChat;

/// <summary>Matches complete @names, @SteamIds and &lt;@SteamId&gt; mentions, without matching email addresses.</summary>
public static class ChatMentionPolicy
{
	public static bool ContainsMention( string text, ulong steamId, string displayName )
	{
		if ( string.IsNullOrEmpty( text ) ) return false;
		var id = steamId.ToString( CultureInfo.InvariantCulture );
		return (steamId != 0 && (ContainsToken( text, "@" + id ) || ContainsToken( text, "<@" + id + ">" )))
			|| (!string.IsNullOrWhiteSpace( displayName ) && ContainsToken( text, "@" + displayName.Trim() ));
	}

	static bool ContainsToken( string text, string token )
	{
		for ( int start = 0; start <= text.Length - token.Length; )
		{
			var index = text.IndexOf( token, start, StringComparison.OrdinalIgnoreCase );
			if ( index < 0 ) return false;
			var end = index + token.Length;
			if ( (index == 0 || !IsNameCharacter( text[index - 1] ))
				&& (end == text.Length || !IsNameCharacter( text[end] )
					|| (text[end] == '.' && (end + 1 == text.Length || char.IsWhiteSpace( text[end + 1] )))) ) return true;
			start = index + 1;
		}
		return false;
	}

	static bool IsNameCharacter( char value ) => char.IsLetterOrDigit( value ) || value is '_' or '@' or '-' or '.';
}

/// <summary>Consumes message identities even while alerts are disabled, so enabling alerts does not replay old messages.</summary>
public sealed class ChatMentionGate
{
	readonly HashSet<(ulong, Guid)> seen = new();
	readonly Queue<(ulong, Guid)> order = new();
	public bool ShouldNotify( Guid id, ulong sender, ulong localUser, string localName, string text, bool live, bool blocked, bool enabled )
	{
		if ( id == Guid.Empty || sender == 0 || !seen.Add( (sender, id) ) ) return false;
		order.Enqueue( (sender, id) );
		if ( order.Count > 256 ) seen.Remove( order.Dequeue() );
		return live && !blocked && enabled && sender != localUser && ChatMentionPolicy.ContainsMention( text, localUser, localName );
	}
}
