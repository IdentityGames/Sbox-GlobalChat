# s&box Editor Global Chat

A dockable global chat window for the s&box editor. It connects to the same Steam global chat as the main menu. You do not need to run your game.

## Install

Requires Windows and an s&box editor build with Razor `PanelWindow` support.

From your project's root folder, run:

```powershell
git clone https://github.com/IdentityGames/Sbox-GlobalChat.git Libraries/GlobalChat
```

Reopen the project so the editor can find the library. Then select **Editor → Chat → Global Chat**.

## Use

- Drag the chat tab to dock it, or drag it out to make a floating window.
- Send messages, read recent history, and share or join party invitations.
- Click a name or avatar to open the user's Steam profile or s&box profile, or add an @mention. Right-click to add a mention directly.
- Use the two-person button to show or hide the user list.
- Open the settings button to enable or disable **Mention popups** and **Mention sound** separately.

New mentions can show a compiler-style popup and play a Windows notification chime. Alerts support `@display name`, `@SteamId`, and `<@SteamId>`. History, duplicate messages, blocked users, and your own messages do not trigger alerts.

Chat stays connected while its dock is open, including when another tab is selected. Closing the dock leaves chat. The editor saves the draft, settings, and dock layout.

## Compatibility and tests

The library uses reflection to access the engine's menu chat service. It uses the normal `dev=0` global channel and matches the engine API and network versions. Changes to private engine APIs can require a library update.

The `editor_global_chat` MCP tools provide connection status, captures, local alert previews, and input checks. These tools do not send public chat messages. Sound and popup previews passed in the editor; party actions with other users still need a live test.

Run the mention policy tests from this repository:

```powershell
dotnet test Tests/GlobalChat.Tests.csproj
```

The chat service and UI are adapted from [Facepunch's s&box source](https://github.com/Facepunch/sbox-public). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for its license notice.
