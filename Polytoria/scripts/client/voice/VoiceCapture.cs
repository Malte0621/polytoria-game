// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using Polytoria.Shared.Voice;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.Voice;

/// <summary>
/// Client microphone capture node. While capturing it pulls raw PCM from the
/// "VoiceMic" bus's AudioEffectCapture every frame, downmixes to mono, resamples from
/// the engine mix rate to <see cref="VoiceConstants.SampleRate"/>, applies input gain,
/// and emits fixed-size 20 ms frames via <see cref="FrameCaptured"/> together with the
/// frame's RMS loudness (so the owner can do voice-activation / metering). It does NOT
/// decide whether to transmit — gating (push-to-talk / VAD / mute) is the service's job.
/// </summary>
public sealed partial class VoiceCapture : Node
{
	/// <summary>Raised once per captured 20 ms frame: (mono PCM, rms in 0..1).</summary>
	public event Action<short[], float>? FrameCaptured;

	/// <summary>Linear microphone gain applied before framing (0 = silent).</summary>
	public float InputGain = 1f;

	public bool Capturing { get; private set; }

	private AudioStreamPlayer? _micPlayer;
	private AudioEffectCapture? _capture;
	private readonly VoiceResampler _resampler = new();
	private readonly List<float> _accum = new();

	public override void _Ready()
	{
		SetProcess(false);
	}

	/// <summary>Open the microphone and begin producing frames. Returns false if the capture bus is unavailable.</summary>
	public bool Start()
	{
		if (Capturing)
		{
			return true;
		}

		VoiceAudio.EnsureMicBus(out _capture);
		if (_capture == null)
		{
			PT.PrintWarn("[Voice] Could not create microphone capture bus.");
			return false;
		}

		int mixRate = (int)AudioServer.GetMixRate();
		_resampler.Reset(mixRate, VoiceConstants.SampleRate);
		_capture.ClearBuffer();
		_accum.Clear();

		_micPlayer = new AudioStreamPlayer
		{
			Stream = new AudioStreamMicrophone(),
			Bus = VoiceConstants.MicBusName
		};
		AddChild(_micPlayer);
		_micPlayer.Play();

		Capturing = true;
		SetProcess(true);
		return true;
	}

	/// <summary>Close the microphone and stop producing frames.</summary>
	public void Stop()
	{
		if (!Capturing)
		{
			return;
		}

		Capturing = false;
		SetProcess(false);

		if (_micPlayer != null)
		{
			_micPlayer.Stop();
			_micPlayer.QueueFree();
			_micPlayer = null;
		}
		_accum.Clear();
	}

	public override void _Process(double delta)
	{
		if (!Capturing || _capture == null)
		{
			return;
		}

		int available = _capture.GetFramesAvailable();
		if (available <= 0)
		{
			return;
		}

		Vector2[] frames = _capture.GetBuffer(available);
		if (frames.Length == 0)
		{
			return;
		}

		// Downmix stereo capture to mono.
		float[] mono = new float[frames.Length];
		for (int i = 0; i < frames.Length; i++)
		{
			mono[i] = (frames[i].X + frames[i].Y) * 0.5f;
		}

		_resampler.Process(mono, mono.Length, _accum);

		int frameSize = VoiceConstants.FrameSamples;
		while (_accum.Count >= frameSize)
		{
			short[] pcm = new short[frameSize];
			double sumSq = 0;
			for (int j = 0; j < frameSize; j++)
			{
				float s = Mathf.Clamp(_accum[j] * InputGain, -1f, 1f);
				sumSq += s * s;
				pcm[j] = (short)Mathf.RoundToInt(s * short.MaxValue);
			}
			_accum.RemoveRange(0, frameSize);

			float rms = (float)Math.Sqrt(sumSq / frameSize);
			FrameCaptured?.Invoke(pcm, rms);
		}
	}
}
