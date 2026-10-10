namespace Editor.GlobalChat;

// Menu chat SCSS, with class selectors for the editor Razor view. Tokens and grain stay engine-owned.
internal static class GlobalChatTheme
{
	internal const string Styles = """
@import "/styles/_front.scss";
.editor-global-chat
{
	@include front-card;
	flex-direction: row;
	flex-shrink: 1;
	min-width: 500px;
	flex-grow: 1;
	min-height: 0;
	font-family: $front-font;
	color: $front-body;
	gap: 0;
	border-radius: $front-radius;
	overflow: hidden;
	.global-conversation
	{
		min-height: 0;
		width: 0;
		flex-shrink: 1;
		flex-grow: 1;
		min-width: 0;
		flex-direction: column;
	}

	.conversation-heading
	{
		align-items: center;
		gap: 8px;
		padding: 6px 16px;
		min-height: 40px;
		border-bottom: 1px solid $front-border;
		flex-shrink: 0;

		> label
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0;
			width: 0;
			font-size: 11px;
			color: $front-secondary;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		button
		{
			@include front-icon-button;
			width: 28px;
			height: 28px;
			padding: 4px;
			icon { font-size: 18px; }
			&.active { color: $front-heading; }
		}
	}

	.chat-notice
	{
		margin: 12px 24px 0;
		padding: 12px;
		background-color: rgba( $front-primary, 0.12 );
		color: $front-body;
		flex-shrink: 0;
	}

	.chat-notice.error
	{
		background-color: rgba( $front-danger, 0.1 );
		color: $front-danger;
	}

	.chat-messages
	{
		flex-grow: 1;
		min-height: 0;
		flex-direction: column;
		overflow-y: scroll;
		overflow-x: hidden;
		padding: $front-inset;
		gap: 18px;
	}

	.chat-empty
	{
		flex-grow: 1;
		align-items: center;
		justify-content: center;
		flex-direction: column;
		gap: 12px;
		color: $front-secondary;
	}

	.chat-empty icon
	{
		font-size: 40px;
	}
}


.chat-message
{
	gap: 12px;
	flex-shrink: 0;

	.chat-avatar
	{
		@include front-row-media;
		cursor: pointer;
		border-radius: 50%;
	}

	.chat-message-body
	{
		flex-direction: column;
		min-width: 0;
		flex-grow: 1;
		gap: 6px;
		word-break: normal;
	}

	.chat-name
	{
		align-self: flex-start;
		cursor: pointer;
		font-weight: 600;
		color: $front-heading;
		font-size: 13px;

		&:hover
		{
			color: $front-link;
		}
	}
}


.chat-members
{
	width: 200px;
	min-width: 200px;
	max-width: 200px;
	min-height: 0;
	border-left: 1px solid $front-border;
	flex-shrink: 0;
	flex-direction: column;
	padding: $front-inset;
	gap: 16px;

	.people-heading
	{
		@include front-section-label;
		justify-content: space-between;
		flex-shrink: 0;
	}

	.chat-people
	{
		// Fill the side column and let the virtual list own its scrolling.
		height: 0;
		width: 100%;
		flex-grow: 1;
		min-height: 0;
		gap: 0;
	}

	.chat-person
	{
		width: 100%;
		height: 44px;
		align-items: center;
		gap: 10px;
		padding: 6px;
		flex-shrink: 0;
		font-size: 13px;
		border-radius: 6px;
		cursor: pointer;

		.chat-avatar
		{
			@include front-row-media;
			width: 28px;
			height: 28px;
			cursor: pointer;
			border-radius: 50%;
		}

		> label
		{
			width: 0;
			flex-grow: 1;
			min-width: 0;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		&:hover
		{
			background-color: rgba( 255, 255, 255, 0.05 );
			sound-in: "ui.button.over";
		}

		&:active
		{
			sound-in: "ui.button.press";
		}
	}
}


.chat-composer-container
{
	flex-direction: column;
	flex-shrink: 0;

	.chat-composer
	{
		margin: 0 $front-inset;
		gap: 8px;
		flex-shrink: 0;
	}

	.chat-composer textentry
	{
		width: 0;
		min-width: 0;
		flex-grow: 1;
		padding: 14px;
		background-color: $front-card-deep;
		border: 1px solid $front-border;
		border-radius: $front-radius;

		&:focus
		{
			border-color: $front-primary;
		}
	}

	.chat-actions
	{
		flex-wrap: wrap;
		padding: $front-inset;
		gap: 12px;
		align-items: center;
		flex-shrink: 0;
		font-size: 13px;
		color: $front-secondary;
	}

	button
	{
		cursor: pointer;

		&:hover
		{
			sound-in: "ui.button.over";
		}

		&:active
		{
			sound-in: "ui.button.press";
		}
	}

	.chat-actions button
	{
		@include front-pill;

		iconpanel
		{
			font-size: 14px;
		}
	}

	.chat-composer button
	{
		@include front-icon-button;
		align-self: center;
	}

	button.disabled
	{
		opacity: 0.3;
		pointer-events: none;
	}
}


.party-invitation
{
	align-self: flex-start;
	max-width: 100%;
	flex-direction: row;
	flex-wrap: wrap;
	align-items: center;
	gap: 16px;
	padding: 12px;
	border: 1px solid $front-border;
	border-radius: $front-radius;
	background-color: $front-card-deep;
	font-family: $front-font;
	word-break: normal;

	.invitation-summary
	{
		flex-direction: column;
		flex-shrink: 1;
		min-width: 0;
		max-width: 100%;
		gap: 4px;
	}

	.invitation-name
	{
		color: $front-heading;
		font-weight: 600;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.invitation-details
	{
		color: $front-secondary;
		font-size: 12px;
	}

	button
	{
		@include front-pill;
		cursor: pointer;

		&:hover
		{
			sound-in: "ui.button.over";
		}

		&:active
		{
			sound-in: "ui.button.press";
		}
	}

	button.primary
	{
		background-color: $front-primary;
		border-color: $front-primary;

		label
		{
			color: $front-heading;
		}

		&:hover
		{
			background-color: lighten( $front-primary, 8% );
		}
	}

	button.disabled
	{
		opacity: 0.3;
		pointer-events: none;
	}
}
.editor-global-chat { width: 100%; height: 100%; min-width: 0; border-radius: 0; font-size: 13px; pointer-events: all; }
.chat-members { width: 184px; min-width: 184px; max-width: 184px; }
.chat-members .chat-people { flex-direction: column; overflow-y: scroll; overflow-x: hidden; }
.chat-settings { padding: 12px 16px; gap: 16px; flex-wrap: wrap; flex-shrink: 0; border-bottom: 1px solid $front-border; font-size: 12px; }
.chat-settings checkbox { gap: 8px; align-items: center; cursor: pointer; }
.chat-settings checkbox .checkmark { width: 16px; height: 16px; border: 1px solid $front-secondary; border-radius: 3px; margin-right: 6px; }
.chat-settings checkbox.checked .checkmark { background-color: $front-primary; border-color: $front-primary; }
.editor-global-chat::after { display: none; background-image: none; }
.editor-global-chat .chat-grain { position: absolute; left: 0; top: 0; right: 0; bottom: 0; background-repeat: repeat; background-size: 512px 512px; opacity: $front-grain-opacity; pointer-events: none; }
.editor-global-chat .global-conversation, .editor-global-chat .chat-members { z-index: 1; }
.editor-global-chat icon { font-family: "Material Icons"; }
.chat-settings checkbox .checkmark { font-family: "Material Icons"; font-size: 14px; color: transparent; }
.chat-settings checkbox.checked .checkmark { color: $front-heading; }
.chat-user-dismiss { position: absolute; left: 0; top: 0; right: 0; bottom: 0; z-index: 10; pointer-events: all; }
.chat-user-actions
{
	position: absolute; left: 8px; top: 8px; z-index: 11;
	width: 224px; padding: 8px; flex-direction: column; gap: 2px;
	background-color: $front-card-deep; border: 1px solid $front-border; border-radius: $front-radius;
	box-shadow: 0px 8px 24px rgba( black, 0.45 );
	.chat-user-heading { align-items: center; padding: 4px 6px 8px; border-bottom: 1px solid $front-border; margin-bottom: 4px; }
	.chat-user-heading > label { flex-grow: 1; flex-shrink: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; color: $front-heading; }
	button { padding: 9px 6px; gap: 10px; align-items: center; border-radius: 4px; cursor: pointer; font-size: 12px; }
	button:hover { background-color: rgba( $front-primary, 0.16 ); color: $front-heading; }
	button.disabled { opacity: 0.35; pointer-events: none; }
	button icon { font-size: 16px; color: $front-secondary; }
	.chat-user-heading button { padding: 2px; }
}
""";
}
