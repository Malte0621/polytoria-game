// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared.Voice;

namespace Polytoria.Client.Voice;

/// <summary>
/// Helpers that lazily create and locate the two runtime audio buses the voice
/// pipeline needs, so we don't have to hand-author them into default_bus_layout.tres:
///   * "Voice" — every positional <see cref="AudioStreamPlayer3D"/> for remote speakers
///     routes here, giving an independent output-volume control.
///   * "VoiceMic" — carries the microphone stream and hosts the AudioEffectCapture used
///     to pull raw PCM. It is muted so the local user never monitors (hears) themselves.
/// </summary>
public static class VoiceAudio
{
	/// <summary>Ensure the "Voice" output bus exists; returns its index.</summary>
	public static int EnsureVoiceBus()
	{
		int idx = AudioServer.GetBusIndex(VoiceConstants.VoiceBusName);
		if (idx != -1)
		{
			return idx;
		}

		AudioServer.AddBus();
		idx = AudioServer.BusCount - 1;
		AudioServer.SetBusName(idx, VoiceConstants.VoiceBusName);
		AudioServer.SetBusSend(idx, "Master");
		return idx;
	}

	/// <summary>
	/// Ensure the muted "VoiceMic" capture bus exists with an AudioEffectCapture on it,
	/// returning its index and the capture effect (null only if creation failed).
	/// </summary>
	public static int EnsureMicBus(out AudioEffectCapture? capture)
	{
		capture = null;
		int idx = AudioServer.GetBusIndex(VoiceConstants.MicBusName);
		if (idx == -1)
		{
			AudioServer.AddBus();
			idx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(idx, VoiceConstants.MicBusName);
			AudioServer.SetBusSend(idx, "Master");
			// Mute so the player never hears their own microphone (the capture effect
			// still receives the bus signal — mute only zeroes the send to Master).
			AudioServer.SetBusMute(idx, true);
		}

		// Find an existing capture effect, or add one.
		int effectCount = AudioServer.GetBusEffectCount(idx);
		for (int e = 0; e < effectCount; e++)
		{
			if (AudioServer.GetBusEffect(idx, e) is AudioEffectCapture existing)
			{
				capture = existing;
				break;
			}
		}

		if (capture == null)
		{
			capture = new AudioEffectCapture();
			AudioServer.AddBusEffect(idx, capture);
		}

		return idx;
	}
}
