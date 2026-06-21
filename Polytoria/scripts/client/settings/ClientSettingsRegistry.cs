// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Enums;
using Polytoria.Shared.Settings;
using System.Collections.Generic;

namespace Polytoria.Client.Settings;

public static class ClientSettingsRegistry
{
	public static readonly IReadOnlyList<SettingSectionDef> Sections =
	[
		new() {Key = "general", Label = "General", IconPath = "res://assets/textures/ui-icons/settings.svg", SortOrder = 0},
		new() {Key = "display", Label = "Display", IconPath = "res://assets/textures/ui-icons/camera.svg", SortOrder = 1},
		new() {Key = "graphics", Label = "Graphics", IconPath = "res://assets/textures/ui-icons/mountain.svg", SortOrder = 2},
		new() {Key = "post_processing", Label = "Post Processing", IconPath = "res://assets/textures/ui-icons/rocket.svg", SortOrder = 3},
		new() {Key = "overlay", Label = "Overlay", IconPath = "res://assets/textures/ui-icons/copy.svg", SortOrder = 4},
		new() {Key = "chat", Label = "Chat", IconPath = "res://assets/textures/ui-icons/messages.svg", SortOrder = 5},
		new() {Key = "advanced", Label = "Advanced", IconPath = "res://assets/textures/ui-icons/code.svg", SortOrder = 6},
		new() {Key = "voice", Label = "Voice Chat", IconPath = "res://assets/textures/ui-icons/messages.svg", SortOrder = 7}
	];

	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>();

		SharedSettingsRegistry.AddSharedTo(defs);

		defs.Add(ClientSettingKeys.Chat.ChatColors,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Chat.ChatColors,
				SectionKey = "chat",
				Label = "Chat Colors",
				Description = "Show colored usernames in chat.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.Chat.ChatFont,
			new SettingDef<string>
			{
				Key = ClientSettingKeys.Chat.ChatFont,
				SectionKey = "chat",
				Label = "Chat Font",
				Description = "Font used for chat messages.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "",
				Options =
				[
					new() { Value = "", Label = "Default" },
					new() { Value = "res://assets/fonts/built-in/SourceSans3-VariableFont_wght.ttf", Label = "Source Sans" },
					new() { Value = "res://assets/fonts/built-in/RobotoMono-VariableFont_wght.ttf", Label = "Roboto Mono" },
					new() { Value = "res://assets/fonts/built-in/Rubik-VariableFont_wght.ttf", Label = "Rubik" },
					new() { Value = "res://assets/fonts/built-in/Poppins/Poppins-Regular.ttf", Label = "Poppins" },
					new() { Value = "res://assets/fonts/built-in/ComicNeue/ComicNeue-Regular.ttf", Label = "Comic Neue" },
					new() { Value = "res://assets/fonts/built-in/PressStart2P-Regular.ttf", Label = "Press Start 2P" },
					new() { Value = "res://assets/fonts/built-in/Comic Sans MS.ttf", Label = "Comic Sans MS" },
					new() { Value = "res://assets/fonts/built-in/Fredoka-VariableFont_wdth,wght.ttf", Label = "Fredoka" },
				]
			});

		defs.Add(ClientSettingKeys.Chat.ChatFontSize,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Chat.ChatFontSize,
				SectionKey = "chat",
				Label = "Chat Font Size",
				Description = "Font size for chat messages. 0 uses the theme default.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0f,
				MinValue = 0f,
				MaxValue = 28f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.General.CtrlLock,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.General.CtrlLock,
				SectionKey = "general",
				Label = "Ctrl Lock",
				Description = "Allow Ctrl Lock while in third person.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.General.MasterVolume,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.General.MasterVolume,
				SectionKey = "general",
				Label = "Volume",
				Description = "Master game volume.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 80f,
				MinValue = 0f,
				MaxValue = 100f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.General.CameraSensitivity,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.General.CameraSensitivity,
				SectionKey = "general",
				Label = "Camera Sensitivity",
				Description = "Camera movement sensitivity.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0.6f,
				MinValue = 0.1f,
				MaxValue = 1.2f,
				Step = 0.1f
			});

		defs.Add(ClientSettingKeys.Display.UiScale,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Display.UiScale,
				SectionKey = "display",
				Label = "UI Scale",
				Description = "Scale of the user interface.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = 1f,
				Options =
				[
					new() { Value = 0.5f, Label = "0.5x" },
					new() { Value = 0.75f, Label = "0.75x" },
					new() { Value = 1f, Label = "1x" },
					new() { Value = 1.25f, Label = "1.25x" },
					new() { Value = 1.5f, Label = "1.5x" },
					new() { Value = 1.75f, Label = "1.75x" },
					new() { Value = 2f, Label = "2x" },
				]
			});

		defs.Add(ClientSettingKeys.Overlay.PerformanceOverlayMode,
			new SettingDef<OverlayMode>
			{
				Key = ClientSettingKeys.Overlay.PerformanceOverlayMode,
				SectionKey = "overlay",
				Label = "Performance Overlay",
				Description = "Show performance information on the screen.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = OverlayMode.None,
				Options =
				[
					new() { Value = OverlayMode.None, Label = "None" },
					new() { Value = OverlayMode.Minimal, Label = "Minimal" },
					new() { Value = OverlayMode.Full, Label = "Full" },
				]
			});

		defs.Add(ClientSettingKeys.Overlay.ConnectionIndicators,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Overlay.ConnectionIndicators,
				SectionKey = "overlay",
				Label = "Show Connection Indicators",
				Description = "Show connection status warnings.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.Advanced.ShowAdvancedSettings,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Advanced.ShowAdvancedSettings,
				SectionKey = "advanced",
				Label = "Show Advanced Settings",
				Description = "Shows hidden advanced settings.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
			});

		defs.Add(ClientSettingKeys.Voice.Enabled,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Voice.Enabled,
				SectionKey = "voice",
				Label = "Enable Voice Chat",
				Description = "Capture your microphone and hear other players' voices.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false
			});

		defs.Add(ClientSettingKeys.Voice.ActivationMode,
			new SettingDef<VoiceActivationModeEnum>
			{
				Key = ClientSettingKeys.Voice.ActivationMode,
				SectionKey = "voice",
				Label = "Activation Mode",
				Description = "When your microphone transmits.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = VoiceActivationModeEnum.VoiceActivity,
				Options =
				[
					new() { Value = VoiceActivationModeEnum.VoiceActivity, Label = "Voice Activity" },
					new() { Value = VoiceActivationModeEnum.PushToTalk, Label = "Push to Talk" },
					new() { Value = VoiceActivationModeEnum.Open, Label = "Open Mic" },
				]
			});

		defs.Add(ClientSettingKeys.Voice.PushToTalkKey,
			new SettingDef<KeyCodeEnum>
			{
				Key = ClientSettingKeys.Voice.PushToTalkKey,
				SectionKey = "voice",
				Label = "Push to Talk Key",
				Description = "Key held to transmit when using Push to Talk.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = KeyCodeEnum.V,
				Options =
				[
					new() { Value = KeyCodeEnum.V, Label = "V" },
					new() { Value = KeyCodeEnum.B, Label = "B" },
					new() { Value = KeyCodeEnum.T, Label = "T" },
					new() { Value = KeyCodeEnum.G, Label = "G" },
					new() { Value = KeyCodeEnum.H, Label = "H" },
					new() { Value = KeyCodeEnum.J, Label = "J" },
					new() { Value = KeyCodeEnum.K, Label = "K" },
					new() { Value = KeyCodeEnum.CapsLock, Label = "Caps Lock" },
				]
			});

		defs.Add(ClientSettingKeys.Voice.ActivationThreshold,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Voice.ActivationThreshold,
				SectionKey = "voice",
				Label = "Activation Threshold",
				Description = "Loudness required to open the mic in Voice Activity mode. Lower is more sensitive.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0.05f,
				MinValue = 0f,
				MaxValue = 0.3f,
				Step = 0.005f
			});

		defs.Add(ClientSettingKeys.Voice.InputVolume,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Voice.InputVolume,
				SectionKey = "voice",
				Label = "Microphone Volume",
				Description = "Gain applied to your microphone input.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 100f,
				MinValue = 0f,
				MaxValue = 200f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.Voice.OutputVolume,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Voice.OutputVolume,
				SectionKey = "voice",
				Label = "Voice Volume",
				Description = "Volume of other players' voices.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 100f,
				MinValue = 0f,
				MaxValue = 200f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.Voice.MaxDistance,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Voice.MaxDistance,
				SectionKey = "voice",
				Label = "Hearing Distance",
				Description = "How far away a speaker can be before their voice fully fades out.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 60f,
				MinValue = 10f,
				MaxValue = 250f,
				Step = 5f
			});

		defs.Add(ClientSettingKeys.Voice.InputDevice,
			new SettingDef<string>
			{
				Key = ClientSettingKeys.Voice.InputDevice,
				SectionKey = "voice",
				Label = "Microphone Device",
				Description = "Input device to capture from. Use the VoiceChat API to pick a specific device.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "Default",
				IsAdvanced = true,
				Options =
				[
					new() { Value = "Default", Label = "Default" },
				]
			});

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
