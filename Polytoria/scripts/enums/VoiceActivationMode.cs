// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Enums;

/// <summary>
/// How the local microphone decides when to transmit. Exposed to Luau as
/// <c>Enums.VoiceActivationMode</c>.
/// </summary>
[ScriptEnum]
public enum VoiceActivationModeEnum
{
	/// <summary>Only transmit while the push-to-talk key is held.</summary>
	PushToTalk,

	/// <summary>Transmit automatically when input loudness crosses the activation threshold.</summary>
	VoiceActivity,

	/// <summary>Always transmit while voice chat is enabled (open mic).</summary>
	Open
}
