// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Shared.Voice;

/// <summary>
/// Shared tuning constants for the spatial voice-chat pipeline. These values are
/// referenced by both the client capture/playback path and the server relay, and
/// MUST stay identical on every peer (sample rate, frame size and the dedicated
/// ENet transfer channel are part of the wire contract).
/// </summary>
public static class VoiceConstants
{
	/// <summary>
	/// Voice transport sample rate (mono). Opus's native full-band rate — best quality,
	/// and Opus bitrate is rate-independent so efficiency is unchanged. (Note: Concentus
	/// 2.2.2's decoder is broken at 24000 Hz, so 48000 is also the correct choice there.)
	/// </summary>
	public const int SampleRate = 48000;

	/// <summary>Length of a single encoded voice frame, in milliseconds.</summary>
	public const int FrameMs = 20;

	/// <summary>Samples per voice frame (SampleRate * FrameMs / 1000).</summary>
	public const int FrameSamples = SampleRate * FrameMs / 1000; // 960

	/// <summary>Target Opus bitrate (bits/sec) for the voice stream.</summary>
	public const int OpusBitrate = 24000;

	/// <summary>Opus encoder complexity (0..10); a middle value balances CPU and quality.</summary>
	public const int OpusComplexity = 5;

	/// <summary>Opus encoder packet-loss-percent hint; tunes FEC redundancy for the unreliable transport.</summary>
	public const int OpusPacketLossPercent = 15;

	/// <summary>
	/// Dedicated ENet channel for voice packets so they never head-of-line block the
	/// transform/prop/chat channels (0/1/2). Requires NetworkInstance maxChannels &gt;= 4.
	/// </summary>
	public const int NetChannel = 3;

	/// <summary>Audio bus that the microphone capture effect lives on (muted, never monitored).</summary>
	public const string MicBusName = "VoiceMic";

	/// <summary>Audio bus that all positional voice playback is routed through (independent output volume).</summary>
	public const string VoiceBusName = "Voice";

	/// <summary>Generator buffer length (seconds) for playback; absorbs network jitter.</summary>
	public const float GeneratorBufferLength = 0.25f;

	/// <summary>How long voice-activation stays open after the last frame above threshold (avoids choppy cut-offs).</summary>
	public const float VadHangoverSeconds = 0.4f;

	/// <summary>How long after the last received frame a remote speaker is still considered "speaking".</summary>
	public const double SpeakingTimeoutSeconds = 0.3;
}
