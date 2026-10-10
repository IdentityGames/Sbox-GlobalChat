using Sandbox;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Editor.GlobalChat;

/// <summary>
/// The only reflection boundary. Uses the engine's Steam query semaphore, membership handles,
/// authenticated callbacks and wire protocol. Does not change engine methods or global settings.
/// </summary>
internal sealed class EngineChatBridge
{
	const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
	readonly Type channelType;
	readonly PropertyInfo lobbyList;
	readonly MethodInfo worldwide, withKeyValue, request, join, create, menuScope;
	readonly PropertyInfo lobbyId;
	readonly FieldInfo steamIdValue;
	readonly Dictionary<string, string> filters;

	public EngineChatBridge()
	{
		var engine = typeof( Scene ).Assembly;
		var matchmaking = RequireType( engine, "Steamworks.SteamMatchmaking" );
		var query = RequireType( engine, "Steamworks.Data.LobbyQuery" );
		var lobby = RequireType( engine, "Steamworks.Data.Lobby" );
		var protocol = RequireType( engine, "Sandbox.Engine.Protocol" );
		var menu = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault( x => x.GetName().Name == "Sandbox.Menu" )
			?? Assembly.Load( "Sandbox.Menu" );
		channelType = RequireType( menu, "Sandbox.MenuChatChannel" );
		lobbyList = RequireProperty( matchmaking, "LobbyList" );
		lobbyId = RequireProperty( lobby, "Id" );
		steamIdValue = lobbyId.PropertyType.GetField( "Value", All )
			?? throw Incompatible( "Steamworks.SteamId.Value" );
		worldwide = RequireMethod( query, "FilterDistanceWorldwide" );
		withKeyValue = RequireMethod( query, "WithKeyValue", typeof( string ), typeof( string ) );
		request = RequireMethod( query, "RequestAsync", typeof( CancellationToken ) );
		join = RequireMethod( channelType, "JoinAsync", typeof( ulong ) );
		create = RequireMethod( channelType, "CreateAsync", typeof( int ), typeof( Dictionary<string, string> ) );
		menuScope = RequireMethod( RequireType( engine, "Sandbox.Engine.GlobalContext" ), "MenuScope" );
		filters = new()
		{
			["menu_protocol"] = "1",
			["api"] = RequireProperty( protocol, "Api" ).GetValue( null ).ToString(),
			["protocol"] = RequireProperty( protocol, "Network" ).GetValue( null ).ToString(),
			["lobby_type"] = "menu_chat",
			["channel"] = "global",
			// Existing normal menu clients still search for this metadata. Never create an editor-only room.
			["dev"] = "0"
		};
		// Resolve the complete handle contract before joining Steam.
		foreach ( var name in new[] { "Id", "Owner", "MemberCount", "Members", "IsJoined" } ) RequireProperty( channelType, name );
		RequireMethod( channelType, "GetData", typeof( string ) );
		RequireMethod( channelType, "SetData", typeof( string ), typeof( string ) );
		RequireMethod( channelType, "Send", typeof( string ) );
		foreach ( var name in new[] { "MessageReceived", "Changed" } )
			if ( channelType.GetEvent( name, All ) is null ) throw Incompatible( $"{channelType.FullName}.{name}" );
	}

	public IDisposable EnterMenuScope() => (IDisposable)Invoke( menuScope, null );

	public async Task<ulong[]> FindAsync( CancellationToken cancellation )
	{
		using var scope = EnterMenuScope();
		var query = Invoke( worldwide, lobbyList.GetValue( null ) );
		foreach ( var pair in filters ) query = Invoke( withKeyValue, query, pair.Key, pair.Value );
		var found = (IEnumerable)await AwaitResult( Invoke( request, query, cancellation ) );
		return found?.Cast<object>().Select( x => (ulong)steamIdValue.GetValue( lobbyId.GetValue( x ) ) ).OrderBy( x => x ).ToArray() ?? [];
	}

	public async Task<ChatChannel> JoinAsync( ulong id )
	{
		using var scope = EnterMenuScope();
		return new ChatChannel( await AwaitResult( Invoke( join, null, id ) ), channelType );
	}

	public async Task<ChatChannel> CreateAsync()
	{
		using var scope = EnterMenuScope();
		return new ChatChannel( await AwaitResult( Invoke( create, null, 250, new Dictionary<string, string>( filters ) ) ), channelType );
	}

	public async Task JoinPartyAsync( ulong id, CancellationToken cancellation )
	{
		using var scope = EnterMenuScope();
		if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "Leave your current party before joining another." );
		var lobbyType = lobbyId.DeclaringType;
		var lobbyConstructor = lobbyType.GetConstructor( All, null, [lobbyId.PropertyType], null ) ?? throw Incompatible( "Lobby constructor" );
		var partyConstructor = typeof( PartyRoom ).GetConstructor( All, null, [lobbyType], null ) ?? throw Incompatible( "PartyRoom constructor" );
		var current = RequireProperty( typeof( PartyRoom ), "Current" );
		var setCurrent = current.GetSetMethod( true ) ?? throw Incompatible( "PartyRoom.Current setter" );
		var joinLobby = RequireMethod( lobbyType, "Join" );
		var leaveLobby = RequireMethod( lobbyType, "Leave" );
		var waitForMembership = RequireMethod( channelType, "WaitForMembership", lobbyType );
		var eventSystem = RequireProperty( RequireType( typeof( Scene ).Assembly, "Sandbox.Event" ), "EventSystem" ).GetValue( null );
		var runInterface = eventSystem.GetType().GetMethods( All ).Single( x => x.Name == "RunInterface" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 )
			.MakeGenericMethod( typeof( PartyRoom.IEventListener ) );

		var entry = await PartyRoom.GetEntryAsync( id );
		cancellation.ThrowIfCancellationRequested();
		if ( entry is not { } available || available.Members >= available.MaxMembers ) throw new InvalidOperationException( "The party is unavailable or full." );
		if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "Your party changed while joining. Please try again." );
		var steamId = Activator.CreateInstance( lobbyId.PropertyType );
		steamIdValue.SetValue( steamId, id );
		var lobby = lobbyConstructor.Invoke( [steamId] );
		// The public party join leaves the current party, then rejects all editor clients.
		// Use the same Steam join and real PartyRoom lifecycle without changing IsEditor.
		var response = await AwaitResult( Invoke( joinLobby, lobby ) );
		if ( response.ToString() != "Success" ) throw new InvalidOperationException( $"Could not join the party: {response}." );
		PartyRoom party = null;
		try
		{
			await (Task)Invoke( waitForMembership, null, lobby );
			cancellation.ThrowIfCancellationRequested();
			if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "Your party changed while joining. Please try again." );
			party = (PartyRoom)partyConstructor.Invoke( [lobby] );
			Invoke( setCurrent, null, party );
			Invoke( runInterface, eventSystem, (Action<PartyRoom.IEventListener>)(listener => listener.OnJoinedParty( party )) );
		}
		catch
		{
			if ( party is not null ) party.Leave();
			else Invoke( leaveLobby, lobby );
			throw;
		}
	}

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

	internal static Type RequireType( Assembly assembly, string name )
	{
		return assembly.GetType( name, false ) ?? throw Incompatible( name );
	}

	internal static PropertyInfo RequireProperty( Type type, string name )
	{
		return type.GetProperty( name, All ) ?? throw Incompatible( $"{type.FullName}.{name}" );
	}

	internal static MethodInfo RequireMethod( Type type, string name, params Type[] parameters )
	{
		return type.GetMethod( name, All, null, parameters, null ) ?? throw Incompatible( $"{type.FullName}.{name}" );
	}

	internal static object Invoke( MethodInfo method, object target, params object[] arguments )
	{
		try { return method.Invoke( target, arguments ); }
		catch ( TargetInvocationException e ) when ( e.InnerException is not null )
		{
			ExceptionDispatchInfo.Capture( e.InnerException ).Throw();
			throw;
		}
	}

	static async Task<object> AwaitResult( object value )
	{
		if ( value is not Task task ) throw Incompatible( "an asynchronous engine method result" );
		await task;
		return RequireProperty( task.GetType(), "Result" ).GetValue( task );
	}

	static InvalidOperationException Incompatible( string member )
	{
		return new( $"Editor Global Chat cannot use this engine version: missing {member}. Update the library's EngineChatBridge." );
	}
}

internal sealed class ChatChannel : IDisposable
{
	readonly object handle;
	readonly PropertyInfo id, owner, members, memberCount, isJoined;
	readonly MethodInfo getData, setData, send;
	readonly EventInfo received, changed;
	readonly Action<Friend, string> onReceived;
	readonly Action onChanged;
	bool disposed;
	public event Action<Friend, string> MessageReceived;
	public event Action Changed;
	public ulong Id => (ulong)id.GetValue( handle );
	public Friend Owner => (Friend)owner.GetValue( handle );
	public int MemberCount => (int)memberCount.GetValue( handle );
	public IEnumerable<Friend> Members => (IEnumerable<Friend>)members.GetValue( handle );
	public bool IsJoined => !disposed && (bool)isJoined.GetValue( handle );

	public ChatChannel( object handle, Type type )
	{
		this.handle = handle;
		id = EngineChatBridge.RequireProperty( type, "Id" );
		owner = EngineChatBridge.RequireProperty( type, "Owner" );
		members = EngineChatBridge.RequireProperty( type, "Members" );
		memberCount = EngineChatBridge.RequireProperty( type, "MemberCount" );
		isJoined = EngineChatBridge.RequireProperty( type, "IsJoined" );
		getData = EngineChatBridge.RequireMethod( type, "GetData", typeof( string ) );
		setData = EngineChatBridge.RequireMethod( type, "SetData", typeof( string ), typeof( string ) );
		send = EngineChatBridge.RequireMethod( type, "Send", typeof( string ) );
		received = type.GetEvent( "MessageReceived" );
		changed = type.GetEvent( "Changed" );
		onReceived = (friend, text) => MessageReceived?.Invoke( friend, text );
		onChanged = () => Changed?.Invoke();
		try
		{
			received.AddEventHandler( handle, onReceived );
			changed.AddEventHandler( handle, onChanged );
		}
		catch
		{
			received.RemoveEventHandler( handle, onReceived );
			((IDisposable)handle).Dispose();
			throw;
		}
	}

	public string GetData( string key ) => (string)EngineChatBridge.Invoke( getData, handle, key );
	public void SetData( string key, string value ) => EngineChatBridge.Invoke( setData, handle, key, value );
	public bool Send( string text ) => (bool)EngineChatBridge.Invoke( send, handle, text );

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		received.RemoveEventHandler( handle, onReceived );
		changed.RemoveEventHandler( handle, onChanged );
		((IDisposable)handle).Dispose();
		MessageReceived = null;
		Changed = null;
	}
}
