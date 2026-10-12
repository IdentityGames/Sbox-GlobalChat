using Sandbox;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.GlobalChat;

internal sealed partial class EngineChatBridge
{
	internal async Task<int> ValidatePrivatePartyAsync()
	{
		using var scope = EnterMenuScope();
		if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "Leave your party before running the private party check." );
		var matchmaking = lobbyList.DeclaringType;
		var lobbyType = lobbyId.DeclaringType;
		var accessType = RequireType( typeof( Scene ).Assembly, "Steamworks.LobbyType" );
		var createLobby = RequireMethod( matchmaking, "CreateLobbyAsync", accessType, typeof( int ) );
		var setData = RequireMethod( lobbyType, "SetData", typeof( string ), typeof( string ) );
		var leave = RequireMethod( lobbyType, "Leave" );
		var lobby = await AwaitResult( Invoke( createLobby, null, Enum.Parse( accessType, "Private" ), 2 ) )
			?? throw new InvalidOperationException( "Steam could not create the private validation party." );
		var id = (ulong)steamIdValue.GetValue( lobbyId.GetValue( lobby ) );
		try
		{
			if ( !(bool)Invoke( setData, lobby, "lobby_type", "party" ) || !(bool)Invoke( setData, lobby, "name", "Editor Global Chat validation" ) )
				throw new InvalidOperationException( "Steam could not set up the private validation party." );
			await JoinPartyAsync( id, CancellationToken.None );
			var party = PartyRoom.Current;
			if ( party?.Id.ValueUnsigned != id || !party.Owner.IsMe || party.MemberCount != 1 )
				throw new InvalidOperationException( "The private validation party did not match the expected membership." );
			return party.MemberCount;
		}
		finally
		{
			if ( PartyRoom.Current?.Id.ValueUnsigned == id ) PartyRoom.Current.Leave();
			else Invoke( leave, lobby );
		}
	}

}
