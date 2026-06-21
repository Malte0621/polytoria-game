// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Polytoria.Shared.Voice;

/// <summary>
/// Streaming linear-interpolation resampler for a single mono float channel.
///
/// The microphone is captured at the engine mix rate (typically 44100/48000 Hz) but
/// the voice transport runs at <see cref="VoiceConstants.SampleRate"/>. This converts
/// continuously across <see cref="Process"/> calls (it carries the fractional read
/// position and the last input sample between calls) so there are no clicks at buffer
/// boundaries. Handles up- and down-sampling for any rate ratio.
/// </summary>
public sealed class VoiceResampler
{
	private double _step;   // input samples advanced per output sample (inRate / outRate)
	private double _phase;  // fractional position between _s0 and the next input sample
	private float _s0;      // previous input sample, retained across calls
	private bool _have0;

	public void Reset(int inRate, int outRate)
	{
		_step = outRate > 0 ? (double)inRate / outRate : 1.0;
		_phase = 0;
		_s0 = 0;
		_have0 = false;
	}

	/// <summary>Append resampled output for <paramref name="len"/> samples of <paramref name="input"/>.</summary>
	public void Process(float[] input, int len, List<float> output)
	{
		int i = 0;
		if (!_have0)
		{
			if (len <= 0)
			{
				return;
			}
			_s0 = input[0];
			_have0 = true;
			i = 1;
			_phase = 0;
		}

		while (i < len)
		{
			float s1 = input[i];
			while (_phase < 1.0)
			{
				output.Add((float)(_s0 + (s1 - _s0) * _phase));
				_phase += _step;
			}
			_phase -= 1.0;
			_s0 = s1;
			i++;
		}
	}
}
