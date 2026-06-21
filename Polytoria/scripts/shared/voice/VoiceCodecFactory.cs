// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Concentus;
using Concentus.Enums;

namespace Polytoria.Shared.Voice;

/// <summary>
/// Creates voice codec instances, preferring Opus and falling back to the self-contained
/// ADPCM codec if Opus is unavailable. A FRESH instance must be created per stream (the
/// Opus decoder is stateful), so callers create one for the local encoder and one per
/// remote speaker's decoder.
///
/// Because the whole client/server is a single managed assembly, the codec choice is
/// deterministic across every peer (Concentus is pure managed — it either initialises
/// everywhere or nowhere), so encoder and decoder always agree without a per-packet tag.
/// </summary>
public static class VoiceCodecFactory
{
	private static bool? _opusAvailable;

	static VoiceCodecFactory()
	{
		// Force the pure-managed path; never attempt to P/Invoke a native libopus (there
		// is none shipped, and it would break NativeAOT on mobile).
		try
		{
			OpusCodecFactory.AttemptToUseNativeLibrary = false;
		}
		catch
		{
			// Older/newer Concentus without this switch — the managed path is the default.
		}
	}

	/// <summary>Whether the Opus codec could be initialised on this platform.</summary>
	public static bool OpusAvailable
	{
		get
		{
			if (_opusAvailable == null)
			{
				try
				{
					IOpusEncoder probe = OpusCodecFactory.CreateEncoder(
						VoiceConstants.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP, null);
					_opusAvailable = probe != null;
				}
				catch
				{
					_opusAvailable = false;
				}
			}
			return _opusAvailable.Value;
		}
	}

	/// <summary>Name of the codec that <see cref="Create"/> will return ("Opus" or "ADPCM").</summary>
	public static string ActiveCodecName => OpusAvailable ? "Opus" : "ADPCM";

	/// <summary>Create a fresh codec instance for one stream.</summary>
	public static IVoiceCodec Create()
	{
		if (OpusAvailable)
		{
			try
			{
				return new OpusVoiceCodec();
			}
			catch
			{
				// Fall through to ADPCM if Opus construction fails unexpectedly.
			}
		}
		return new AdpcmVoiceCodec();
	}
}
