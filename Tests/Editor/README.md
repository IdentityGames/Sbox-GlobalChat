# Live editor validation

These checks require a running Windows s&box editor. They do not run under MSTest and are not loaded by the installed library.

For a local validation session, copy the C# files from this folder into `Editor/Validation/` and let the editor compile them. The additional `editor_global_chat` MCP tools then provide keyboard and dock checks, UI checks, captures, alert previews, and a private party check. Remove the copied files when the session is complete. Do not commit the copies.

The checks do not send public chat messages. Input checks restore the draft and settings. The private party check refuses to run when already in a party and leaves its test party when finished.

To compile the validation source without loading it into the editor:

```powershell
dotnet build Editor/globalchat.editor.csproj -p:IncludeEditorValidation=true -p:SandboxRoot="C:/path/to/sbox/game"
```

Run this command from the library root. Compilation alone does not verify live editor behavior.
