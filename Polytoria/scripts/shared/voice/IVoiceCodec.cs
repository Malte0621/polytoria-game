// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Shared.Voice;

/// <summary>
/// Encodes/decodes a single voice frame of 16-bit mono PCM. Implementations MUST
/// produce self-contained packets (each <see cref="Encode"/> output decodable on its
/// own) so the unreliable voice transport can lose/​reorder packets without desyncing.
/// A single encoder instance is used per outbound stream; <see cref="Decode"/> must be
/// safe to call from multiple receivers (one per remote speaker).
///
/// The default implementation is <see cref="AdpcmVoiceCodec"/> (self-contained IMA
/// ADPCM, pure managed, NativeAOT-safe). A higher-quality backend (e.g. Opus) can be
/// dropped in behind this interface without touching the transport or service layers.
/// </summary>
public interface IVoiceCodec
{
	/// <summary>Encode one frame of mono PCM samples into a packet.</summary>
	byte[] Encode(short[] pcm);

	/// <summary>Decode a packet produced by <see cref="Encode"/> back into mono PCM samples.</summary>
	short[] Decode(byte[] data);

	/// <summary>
	/// Produce one frame of packet-loss concealment, called when the receiver detects a
	/// gap in the packet sequence. Stateful codecs (Opus) synthesise a concealment frame
	/// from prior state; stateless ones may return silence. A decoder instance is
	/// per-remote-speaker, so concealment state stays per stream.
	/// </summary>
	short[] DecodeLost();

	/// <summary>
	/// Recover a single lost frame using in-band forward-error-correction carried in the
	/// NEXT received packet (called for the most-recent lost frame on a sequence gap).
	/// Codecs without FEC return concealment/silence.
	/// </summary>
	short[] DecodeFec(byte[] nextPacket);
}
