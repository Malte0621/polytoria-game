// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Concentus;
using Concentus.Enums;
using System;

namespace Polytoria.Shared.Voice;

/// <summary>
/// Opus voice codec (via the pure-managed Concentus port — no native binary, so it is
/// NativeAOT/trimming-safe on desktop and mobile). At ~24 kbps it is roughly 4x smaller
/// than the ADPCM fallback for the same 24 kHz mono voice frame, with better quality and
/// built-in packet-loss concealment.
///
/// The Opus DECODER is stateful across frames, so one codec instance belongs to exactly
/// one stream: the local capture uses one instance for <see cref="Encode"/>, and each
/// remote speaker gets its OWN instance for <see cref="Decode"/>/<see cref="DecodeLost"/>.
/// Encoder and decoder are created lazily so an instance only allocates the side it uses.
/// </summary>
public sealed class OpusVoiceCodec : IVoiceCodec
{
	private const int MaxPacketBytes = 4000;

	private IOpusEncoder? _encoder;
	private IOpusDecoder? _decoder;
	private readonly byte[] _encodeBuffer = new byte[MaxPacketBytes];
	private readonly short[] _decodeBuffer = new short[VoiceConstants.FrameSamples];

	private IOpusEncoder Encoder
	{
		get
		{
			if (_encoder == null)
			{
				_encoder = OpusCodecFactory.CreateEncoder(VoiceConstants.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP, null);
				_encoder.Bitrate = VoiceConstants.OpusBitrate;
				_encoder.Complexity = VoiceConstants.OpusComplexity;
				_encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
				_encoder.UseInbandFEC = true;
				_encoder.PacketLossPercent = VoiceConstants.OpusPacketLossPercent;
			}
			return _encoder;
		}
	}

	private IOpusDecoder Decoder
	{
		get
		{
			_decoder ??= OpusCodecFactory.CreateDecoder(VoiceConstants.SampleRate, 1, null);
			return _decoder;
		}
	}

	public byte[] Encode(short[] pcm)
	{
		if (pcm == null || pcm.Length == 0)
		{
			return Array.Empty<byte>();
		}

		int written = Encoder.Encode(pcm.AsSpan(0, pcm.Length), pcm.Length, _encodeBuffer, _encodeBuffer.Length);
		if (written <= 0)
		{
			return Array.Empty<byte>();
		}

		byte[] outBuf = new byte[written];
		Buffer.BlockCopy(_encodeBuffer, 0, outBuf, 0, written);
		return outBuf;
	}

	public short[] Decode(byte[] data)
	{
		if (data == null || data.Length == 0)
		{
			return Array.Empty<short>();
		}
		return SafeDecode(data, false);
	}

	public short[] DecodeLost()
	{
		// Empty input drives Opus packet-loss concealment for one frame.
		return SafeDecode(null, false);
	}

	public short[] DecodeFec(byte[] nextPacket)
	{
		if (nextPacket == null || nextPacket.Length == 0)
		{
			return Array.Empty<short>();
		}
		// decode_fec=true reconstructs the previous lost frame from the redundancy in the
		// next packet; Opus falls back to concealment when the packet carries no FEC.
		return SafeDecode(nextPacket, true);
	}

	private short[] SafeDecode(byte[]? data, bool fec)
	{
		try
		{
			ReadOnlySpan<byte> input = data == null ? ReadOnlySpan<byte>.Empty : data.AsSpan();
			int samples = Decoder.Decode(input, _decodeBuffer, VoiceConstants.FrameSamples, fec);
			return CopySamples(samples);
		}
		catch (Exception)
		{
			// Voice packets are fully peer-controlled; a corrupt/oversized frame makes the
			// Opus decoder throw. Drop the frame instead (the ADPCM codec's contract too).
			return Array.Empty<short>();
		}
	}

	private short[] CopySamples(int samples)
	{
		if (samples <= 0)
		{
			return Array.Empty<short>();
		}
		short[] outBuf = new short[samples];
		Array.Copy(_decodeBuffer, outBuf, samples);
		return outBuf;
	}
}
