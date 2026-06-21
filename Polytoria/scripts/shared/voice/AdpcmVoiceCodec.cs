// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Shared.Voice;

/// <summary>
/// Self-contained IMA/DVI ADPCM voice codec (4 bits/sample, ~4:1 vs 16-bit PCM).
///
/// Each packet carries its own header: [uint16 sampleCount][int16 firstSample]
/// [uint8 stepIndex] followed by the packed 4-bit codes for the remaining samples.
/// Because the predictor and step index are seeded from the header on every packet,
/// any packet decodes independently of the others — a lost or reordered frame only
/// costs a single 20 ms glitch instead of desyncing the stream, which is exactly
/// what the unreliable voice transport needs.
///
/// The encoder keeps a running step index across frames (better adaptation/quality)
/// but writes that index into each frame header so decoding stays self-contained.
/// One encoder instance is used per outbound stream; <see cref="Decode"/> is pure and
/// re-entrant, so a single instance can decode every remote speaker concurrently.
///
/// Pure managed, allocation-light, NativeAOT/trimming-safe.
/// </summary>
public sealed class AdpcmVoiceCodec : IVoiceCodec
{
	private static readonly int[] IndexTable =
	{
		-1, -1, -1, -1, 2, 4, 6, 8,
		-1, -1, -1, -1, 2, 4, 6, 8
	};

	private static readonly int[] StepTable =
	{
		7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
		19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
		50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
		130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
		337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
		876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
		2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
		5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
		15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
	};

	private const int MaxStepIndex = 88;
	private const int HeaderSize = 5; // u16 count + i16 first + u8 index

	// Encoder-only running step index (carried across frames for smoother adaptation).
	private int _encStepIndex;

	public byte[] Encode(short[] pcm)
	{
		int n = pcm?.Length ?? 0;
		if (n == 0)
		{
			return Array.Empty<byte>();
		}

		// HeaderSize + ceil((n-1)/2) packed code bytes.
		int payloadBytes = (n - 1 + 1) / 2;
		byte[] outBuf = new byte[HeaderSize + payloadBytes];

		int predictor = pcm[0];
		int index = Math.Clamp(_encStepIndex, 0, MaxStepIndex);

		// Header (little-endian).
		ushort count = (ushort)n;
		outBuf[0] = (byte)(count & 0xFF);
		outBuf[1] = (byte)((count >> 8) & 0xFF);
		ushort first = (ushort)predictor;
		outBuf[2] = (byte)(first & 0xFF);
		outBuf[3] = (byte)((first >> 8) & 0xFF);
		outBuf[4] = (byte)index;

		int outPos = HeaderSize;
		bool lowNibble = true;
		byte cur = 0;

		for (int i = 1; i < n; i++)
		{
			int step = StepTable[index];
			int diff = pcm[i] - predictor;
			int code = 0;
			if (diff < 0)
			{
				code = 8;
				diff = -diff;
			}

			int temp = step;
			if (diff >= temp) { code |= 4; diff -= temp; }
			temp >>= 1;
			if (diff >= temp) { code |= 2; diff -= temp; }
			temp >>= 1;
			if (diff >= temp) { code |= 1; }

			// Reconstruct exactly as the decoder will, so predictor stays in lock-step.
			int diffq = step >> 3;
			if ((code & 4) != 0) diffq += step;
			if ((code & 2) != 0) diffq += step >> 1;
			if ((code & 1) != 0) diffq += step >> 2;

			if ((code & 8) != 0)
			{
				predictor -= diffq;
			}
			else
			{
				predictor += diffq;
			}
			predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);

			index += IndexTable[code];
			index = Math.Clamp(index, 0, MaxStepIndex);

			if (lowNibble)
			{
				cur = (byte)(code & 0x0F);
				lowNibble = false;
			}
			else
			{
				cur |= (byte)((code & 0x0F) << 4);
				outBuf[outPos++] = cur;
				lowNibble = true;
			}
		}

		if (!lowNibble)
		{
			outBuf[outPos++] = cur;
		}

		_encStepIndex = index;
		return outBuf;
	}

	// Each ADPCM packet is self-contained (no inter-frame decoder state), so the best
	// concealment for a lost frame is a frame of silence.
	public short[] DecodeLost()
	{
		return new short[VoiceConstants.FrameSamples];
	}

	// ADPCM carries no inter-packet redundancy, so there is nothing to recover; conceal
	// the lost frame with silence (same as DecodeLost).
	public short[] DecodeFec(byte[] nextPacket)
	{
		return new short[VoiceConstants.FrameSamples];
	}

	public short[] Decode(byte[] data)
	{
		if (data == null || data.Length < HeaderSize)
		{
			return Array.Empty<short>();
		}

		int n = data[0] | (data[1] << 8);
		if (n <= 0)
		{
			return Array.Empty<short>();
		}

		int predictor = (short)(data[2] | (data[3] << 8));
		int index = Math.Clamp((int)data[4], 0, MaxStepIndex);

		// Guard against a truncated/malformed packet.
		int needBytes = HeaderSize + ((n - 1 + 1) / 2);
		if (data.Length < needBytes)
		{
			return Array.Empty<short>();
		}

		short[] outBuf = new short[n];
		outBuf[0] = (short)predictor;

		int inPos = HeaderSize;
		bool lowNibble = true;

		for (int i = 1; i < n; i++)
		{
			int code;
			if (lowNibble)
			{
				code = data[inPos] & 0x0F;
				lowNibble = false;
			}
			else
			{
				code = (data[inPos] >> 4) & 0x0F;
				inPos++;
				lowNibble = true;
			}

			int step = StepTable[index];
			int diffq = step >> 3;
			if ((code & 4) != 0) diffq += step;
			if ((code & 2) != 0) diffq += step >> 1;
			if ((code & 1) != 0) diffq += step >> 2;

			if ((code & 8) != 0)
			{
				predictor -= diffq;
			}
			else
			{
				predictor += diffq;
			}
			predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);

			index += IndexTable[code];
			index = Math.Clamp(index, 0, MaxStepIndex);

			outBuf[i] = (short)predictor;
		}

		return outBuf;
	}
}
