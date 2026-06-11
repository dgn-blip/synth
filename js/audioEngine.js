/**
 * audioEngine.js — Web Audio API abstraction for the Fable synthesizer.
 *
 * All Web Audio objects (AudioContext, nodes, buffers, voices) are created
 * and owned here. The F# side only calls these static functions with plain
 * data; it never touches audio nodes directly.
 *
 * Audio graph:
 *
 *   voices (osc + gain) ──► synthLayerGain ──┐
 *   sample sources ───────► sampleLayerGain ─┤
 *                                            ▼
 *                                       filterNode (biquad)
 *                                            │
 *                              ┌─────────────┴─────────────┐
 *                       fxBypassGain                  fxInputGain
 *                              │                           │
 *                              │            distortion → delay → reverb
 *                              │                           │
 *                              └────────────► masterGain ◄─┘
 *                                            │
 *                                       destination
 */

// ---------------------------------------------------------------------------
// Module-level state (singleton — there is exactly one synth per page)
// ---------------------------------------------------------------------------

let ctx = null;

// Static graph nodes
let synthLayerGain = null;
let sampleLayerGain = null;
let filterNode = null;
let fxBypassGain = null;
let fxInputGain = null;
let masterGain = null;

// Distortion stage
let distortionShaper = null;
let distortionTone = null;
let distortionOut = null;

// Delay stage
let delayDry = null;
let delayWet = null;
let delayNode = null;
let delayFeedback = null;
let delayOut = null;

// Reverb stage
let reverbDry = null;
let reverbWet = null;
let convolver = null;

// Cached parameters (kept in sync by the F# update function)
const params = {
  oscillators: [
    { enabled: true, waveType: "sawtooth", coarseTune: 0, fineTune: 0, volume: 0.6 },
    { enabled: false, waveType: "sawtooth", coarseTune: 0, fineTune: 0, volume: 0.6 },
    { enabled: false, waveType: "sawtooth", coarseTune: 0, fineTune: 0, volume: 0.6 },
  ],
  envelope: { attack: 0.02, decay: 0.15, sustain: 0.7, release: 0.3 },
  filter: { type: "lowpass", cutoff: 12000, q: 0.7 },
  filterEnvelope: { attack: 0.05, decay: 0.3, sustain: 0.5, release: 0.4, amount: 0 },
  polyphonyLimit: 16,
};

// Active synth voices: midiNote -> { oscs: OscillatorNode[], gain: GainNode }
const voices = new Map();
// Insertion-ordered list of active note numbers, used for oldest-note stealing.
const voiceOrder = [];

// Registered samples: sampleId -> { buffer, mode, volume, mappedNotes: Set<int> }
const samples = new Map();
// Playing sample sources: "sampleId:note" -> { source, gain }
const playingSamples = new Map();

const MIN_FREQ = 20;
const MAX_FREQ = 20000;

/** Convert a MIDI note number to a frequency in Hz (A4 = 440 Hz = note 69). */
function midiToFrequency(note) {
  return 440 * Math.pow(2, (note - 69) / 12);
}

/** Build a soft-clipping waveshaper curve. drive 0..1 controls intensity. */
function makeDistortionCurve(drive) {
  const k = drive * 100;
  const n = 1024;
  const curve = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    const x = (i * 2) / n - 1;
    // Classic arctangent-style soft clipper; k = 0 is a straight line (clean).
    curve[i] = ((1 + k) * x) / (1 + k * Math.abs(x));
  }
  return curve;
}

/**
 * Generate a synthetic impulse response for the reverb ConvolverNode.
 * roomSize 0..1 scales decay time from ~0.3s to ~4s.
 */
function makeImpulseResponse(roomSize) {
  const duration = 0.3 + roomSize * 3.7;
  const decay = 1.5 + roomSize * 4.5;
  const rate = ctx.sampleRate;
  const length = Math.max(1, Math.floor(rate * duration));
  const impulse = ctx.createBuffer(2, length, rate);
  for (let channel = 0; channel < 2; channel++) {
    const data = impulse.getChannelData(channel);
    for (let i = 0; i < length; i++) {
      // Exponentially decaying white noise approximates a room tail.
      data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / length, decay);
    }
  }
  return impulse;
}

export class AudioEngine {
  // -------------------------------------------------------------------------
  // Initialization
  // -------------------------------------------------------------------------

  /**
   * Create the AudioContext and the full static audio graph.
   * Safe to call more than once. Returns false if Web Audio is unsupported.
   */
  static createAudioContext() {
    if (ctx) return true;
    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) {
      console.error("[AudioEngine] Web Audio API is not supported in this browser.");
      return false;
    }
    ctx = new Ctor();
    AudioEngine.connectAudioGraph();
    return true;
  }

  static getAudioContext() {
    return ctx;
  }

  /** Resume the context — must run inside a user gesture in most browsers. */
  static resume() {
    if (ctx && ctx.state === "suspended") {
      ctx.resume().catch((err) => console.error("[AudioEngine] resume failed:", err));
    }
  }

  /** Build the static node graph (layers, filter, effects, master). */
  static connectAudioGraph() {
    masterGain = ctx.createGain();
    masterGain.gain.value = 0.8;
    masterGain.connect(ctx.destination);

    // Layer busses
    synthLayerGain = ctx.createGain();
    synthLayerGain.gain.value = 0.8;
    sampleLayerGain = ctx.createGain();
    sampleLayerGain.gain.value = 0.8;

    // Shared filter — both layers run through it so the filter section
    // shapes synth and samples alike.
    filterNode = ctx.createBiquadFilter();
    filterNode.type = params.filter.type;
    filterNode.frequency.value = params.filter.cutoff;
    filterNode.Q.value = params.filter.q;
    synthLayerGain.connect(filterNode);
    sampleLayerGain.connect(filterNode);

    // Effects routing: filter splits into a bypass path and the fx chain.
    fxBypassGain = ctx.createGain();
    fxBypassGain.gain.value = 0; // effects enabled by default
    fxInputGain = ctx.createGain();
    fxInputGain.gain.value = 1;
    filterNode.connect(fxBypassGain);
    filterNode.connect(fxInputGain);
    fxBypassGain.connect(masterGain);

    // --- Distortion: input -> shaper -> tone lowpass -> out -----------------
    distortionShaper = ctx.createWaveShaper();
    distortionShaper.curve = makeDistortionCurve(0);
    distortionShaper.oversample = "4x";
    distortionTone = ctx.createBiquadFilter();
    distortionTone.type = "lowpass";
    distortionTone.frequency.value = 8000;
    distortionOut = ctx.createGain();
    fxInputGain.connect(distortionShaper);
    distortionShaper.connect(distortionTone);
    distortionTone.connect(distortionOut);

    // --- Delay: dry + (delay with feedback) wet ------------------------------
    delayDry = ctx.createGain();
    delayWet = ctx.createGain();
    delayNode = ctx.createDelay(2.0);
    delayFeedback = ctx.createGain();
    delayOut = ctx.createGain();
    delayDry.gain.value = 1;
    delayWet.gain.value = 0;
    delayNode.delayTime.value = 0.35;
    delayFeedback.gain.value = 0.3;
    distortionOut.connect(delayDry);
    distortionOut.connect(delayNode);
    delayNode.connect(delayFeedback);
    delayFeedback.connect(delayNode); // feedback loop
    delayNode.connect(delayWet);
    delayDry.connect(delayOut);
    delayWet.connect(delayOut);

    // --- Reverb: dry + convolver wet -----------------------------------------
    reverbDry = ctx.createGain();
    reverbWet = ctx.createGain();
    convolver = ctx.createConvolver();
    convolver.buffer = makeImpulseResponse(0.3);
    reverbDry.gain.value = 0.85;
    reverbWet.gain.value = 0.15;
    delayOut.connect(reverbDry);
    delayOut.connect(convolver);
    convolver.connect(reverbWet);
    reverbDry.connect(masterGain);
    reverbWet.connect(masterGain);
  }

  /** Tear everything down (not used during normal operation). */
  static disconnectAll() {
    AudioEngine.allNotesOff();
    if (ctx) {
      ctx.close().catch(() => {});
      ctx = null;
    }
  }

  // -------------------------------------------------------------------------
  // Note handling (polyphonic)
  // -------------------------------------------------------------------------

  /**
   * Start a note: spawns one OscillatorNode per enabled oscillator plus a
   * per-voice envelope gain, and triggers any matching samples.
   * @param {number} midiNote 0-127
   * @param {number} velocity 0.0-1.0
   */
  static noteOn(midiNote, velocity) {
    if (!ctx) return;
    AudioEngine.resume();

    // Re-trigger: release an existing voice on the same note first.
    if (voices.has(midiNote)) {
      AudioEngine.noteOff(midiNote);
    }

    // Polyphony limit: steal the oldest voice when at capacity.
    while (voiceOrder.length >= params.polyphonyLimit && voiceOrder.length > 0) {
      const oldest = voiceOrder[0];
      AudioEngine.noteOff(oldest);
      // noteOff removes the entry from voiceOrder; guard against stalls.
      if (voiceOrder[0] === oldest) voiceOrder.shift();
    }

    const now = ctx.currentTime;
    const env = params.envelope;
    const vel = Math.max(0.001, Math.min(1, velocity));

    // Per-voice envelope gain
    const voiceGain = ctx.createGain();
    voiceGain.gain.cancelScheduledValues(now);
    voiceGain.gain.setValueAtTime(0.0001, now);
    voiceGain.gain.linearRampToValueAtTime(vel, now + Math.max(0.001, env.attack));
    voiceGain.gain.linearRampToValueAtTime(
      Math.max(0.0001, vel * env.sustain),
      now + Math.max(0.001, env.attack) + Math.max(0.001, env.decay)
    );
    voiceGain.connect(synthLayerGain);

    // One oscillator per enabled slot
    const oscs = [];
    params.oscillators.forEach((op) => {
      if (!op.enabled || op.volume <= 0) return;
      const osc = ctx.createOscillator();
      osc.type = op.waveType;
      // coarseTune is whole semitones, fineTune is fractional semitones.
      const detuned = midiNote + op.coarseTune + op.fineTune;
      osc.frequency.value = midiToFrequency(detuned);
      const oscGain = ctx.createGain();
      oscGain.gain.value = op.volume;
      osc.connect(oscGain);
      oscGain.connect(voiceGain);
      osc.start(now);
      oscs.push(osc);
    });

    voices.set(midiNote, { oscs, gain: voiceGain });
    voiceOrder.push(midiNote);

    // Filter envelope (shared filter — retriggers on every note, which is a
    // deliberate MVP simplification).
    AudioEngine._triggerFilterEnvelope(now);

    // Trigger samples
    samples.forEach((sample, sampleId) => {
      const shouldPlay =
        sample.mode === "pitched" || sample.mappedNotes.has(midiNote);
      if (shouldPlay && sample.buffer) {
        AudioEngine._playSampleVoice(sampleId, sample, midiNote, vel);
      }
    });
  }

  /** Release a note: run the release stage then clean up the voice. */
  static noteOff(midiNote) {
    if (!ctx) return;
    const now = ctx.currentTime;
    const release = Math.max(0.01, params.envelope.release);

    const voice = voices.get(midiNote);
    if (voice) {
      voice.gain.gain.cancelScheduledValues(now);
      voice.gain.gain.setValueAtTime(voice.gain.gain.value, now);
      voice.gain.gain.linearRampToValueAtTime(0.0001, now + release);
      voice.oscs.forEach((osc) => {
        try {
          osc.stop(now + release + 0.05);
        } catch (_) {
          /* already stopped */
        }
      });
      // Disconnect after the tail to free nodes.
      setTimeout(() => {
        try {
          voice.gain.disconnect();
        } catch (_) {}
      }, (release + 0.1) * 1000);
      voices.delete(midiNote);
      const idx = voiceOrder.indexOf(midiNote);
      if (idx >= 0) voiceOrder.splice(idx, 1);
    }

    // Filter envelope release back to base cutoff.
    if (voices.size === 0 && params.filterEnvelope.amount > 0) {
      const fEnv = params.filterEnvelope;
      filterNode.frequency.cancelScheduledValues(now);
      filterNode.frequency.setValueAtTime(filterNode.frequency.value, now);
      filterNode.frequency.linearRampToValueAtTime(
        params.filter.cutoff,
        now + Math.max(0.01, fEnv.release)
      );
    }

    // Release matching sample voices.
    playingSamples.forEach((entry, key) => {
      if (key.endsWith(`:${midiNote}`)) {
        AudioEngine._releaseSampleVoice(key, entry, now, release);
      }
    });
  }

  /** Immediately silence everything (panic / cleanup). */
  static allNotesOff() {
    if (!ctx) return;
    voices.forEach((_, note) => AudioEngine.noteOff(note));
    playingSamples.forEach((entry, key) => {
      AudioEngine._releaseSampleVoice(key, entry, ctx.currentTime, 0.05);
    });
  }

  /** Schedule the filter envelope attack/decay on note-on. */
  static _triggerFilterEnvelope(now) {
    const fEnv = params.filterEnvelope;
    if (fEnv.amount <= 0) return;
    const base = params.filter.cutoff;
    // The envelope sweeps from the base cutoff toward 20 kHz, scaled by amount.
    const peak = Math.min(MAX_FREQ, base + fEnv.amount * (MAX_FREQ - base));
    const sustainFreq = base + (peak - base) * fEnv.sustain;
    filterNode.frequency.cancelScheduledValues(now);
    filterNode.frequency.setValueAtTime(base, now);
    filterNode.frequency.linearRampToValueAtTime(peak, now + Math.max(0.005, fEnv.attack));
    filterNode.frequency.linearRampToValueAtTime(
      sustainFreq,
      now + Math.max(0.005, fEnv.attack) + Math.max(0.005, fEnv.decay)
    );
  }

  // -------------------------------------------------------------------------
  // Samples
  // -------------------------------------------------------------------------

  /**
   * Decode an uploaded File into an AudioBuffer.
   * @param {File} file
   * @returns {Promise<AudioBuffer>}
   */
  static loadSampleFromFile(file) {
    if (!ctx) {
      return Promise.reject(new Error("Audio engine is not initialized yet."));
    }
    return file
      .arrayBuffer()
      .then((data) => ctx.decodeAudioData(data))
      .catch((err) => {
        console.error("[AudioEngine] sample decode failed:", err);
        throw new Error(
          `Could not decode "${file.name}". Use a WAV, MP3, OGG or similar audio file.`
        );
      });
  }

  /** Register a decoded sample so noteOn can trigger it. */
  static registerSample(sampleId, buffer, mode, volume) {
    samples.set(sampleId, {
      buffer,
      mode: mode || "pitched",
      volume: typeof volume === "number" ? volume : 0.8,
      mappedNotes: new Set(),
    });
  }

  static removeSample(sampleId) {
    // Stop any playing instances first.
    playingSamples.forEach((entry, key) => {
      if (key.startsWith(`${sampleId}:`)) {
        AudioEngine._releaseSampleVoice(key, entry, ctx ? ctx.currentTime : 0, 0.05);
      }
    });
    samples.delete(sampleId);
  }

  static setSampleMode(sampleId, mode) {
    const s = samples.get(sampleId);
    if (s) s.mode = mode;
  }

  static setSamplePlaybackVolume(sampleId, volume) {
    const s = samples.get(sampleId);
    if (s) s.volume = volume;
  }

  /** Replace the sample's note mapping with a single MIDI note. */
  static setSampleNoteMapping(sampleId, midiNote) {
    const s = samples.get(sampleId);
    if (s) {
      s.mappedNotes = new Set([midiNote]);
    }
  }

  /** Internal: start one sample playback voice. */
  static _playSampleVoice(sampleId, sample, midiNote, velocity) {
    const key = `${sampleId}:${midiNote}`;
    // Re-trigger: stop the previous instance on this note.
    const existing = playingSamples.get(key);
    if (existing) {
      AudioEngine._releaseSampleVoice(key, existing, ctx.currentTime, 0.02);
    }

    const source = ctx.createBufferSource();
    source.buffer = sample.buffer;
    if (sample.mode === "pitched") {
      // Pitch-shift relative to middle C (60) via playback rate.
      source.playbackRate.value = Math.pow(2, (midiNote - 60) / 12);
    }
    const gain = ctx.createGain();
    gain.gain.value = sample.volume * velocity;
    source.connect(gain);
    gain.connect(sampleLayerGain);
    source.start();
    source.onended = () => {
      playingSamples.delete(key);
      try {
        gain.disconnect();
      } catch (_) {}
    };
    playingSamples.set(key, { source, gain });
  }

  /** Internal: fade out and stop one sample voice. */
  static _releaseSampleVoice(key, entry, now, release) {
    try {
      entry.gain.gain.cancelScheduledValues(now);
      entry.gain.gain.setValueAtTime(entry.gain.gain.value, now);
      entry.gain.gain.linearRampToValueAtTime(0.0001, now + release);
      entry.source.stop(now + release + 0.05);
    } catch (_) {
      /* source may have ended already */
    }
    playingSamples.delete(key);
  }

  // -------------------------------------------------------------------------
  // Parameter setters (called on every slider move — must be cheap)
  // -------------------------------------------------------------------------

  /** Update one oscillator slot. Affects voices started afterwards. */
  static setOscillatorParams(index, p) {
    if (index < 0 || index >= params.oscillators.length) return;
    params.oscillators[index] = {
      enabled: !!p.enabled,
      waveType: p.waveType,
      coarseTune: p.coarseTune | 0,
      fineTune: +p.fineTune,
      volume: +p.volume,
    };
  }

  static setAmplitudeEnvelope(env) {
    params.envelope = {
      attack: +env.attack,
      decay: +env.decay,
      sustain: +env.sustain,
      release: +env.release,
    };
  }

  static setFilterParams(p) {
    params.filter = { type: p.type, cutoff: +p.cutoff, q: +p.q };
    if (!ctx) return;
    filterNode.type = p.type;
    // setTargetAtTime gives a tiny smoothing window so slider drags don't zipper.
    filterNode.frequency.setTargetAtTime(
      Math.max(MIN_FREQ, Math.min(MAX_FREQ, +p.cutoff)),
      ctx.currentTime,
      0.01
    );
    filterNode.Q.setTargetAtTime(Math.max(0.0001, +p.q), ctx.currentTime, 0.01);
  }

  static updateFilterEnvelope(env) {
    params.filterEnvelope = {
      attack: +env.attack,
      decay: +env.decay,
      sustain: +env.sustain,
      release: +env.release,
      amount: +env.amount,
    };
  }

  static setReverbParams(p) {
    if (!ctx) return;
    convolver.buffer = makeImpulseResponse(Math.max(0, Math.min(1, +p.roomSize)));
    const wet = Math.max(0, Math.min(1, +p.wetDry));
    reverbWet.gain.setTargetAtTime(wet, ctx.currentTime, 0.01);
    reverbDry.gain.setTargetAtTime(1 - wet, ctx.currentTime, 0.01);
  }

  static setDelayParams(p) {
    if (!ctx) return;
    delayNode.delayTime.setTargetAtTime(
      Math.max(0.01, Math.min(2, +p.time)),
      ctx.currentTime,
      0.01
    );
    delayFeedback.gain.setTargetAtTime(
      Math.max(0, Math.min(0.9, +p.feedback)),
      ctx.currentTime,
      0.01
    );
    const wet = Math.max(0, Math.min(1, +p.wetDry));
    delayWet.gain.setTargetAtTime(wet, ctx.currentTime, 0.01);
    delayDry.gain.setTargetAtTime(1 - wet, ctx.currentTime, 0.01);
  }

  static setDistortionParams(p) {
    if (!ctx) return;
    distortionShaper.curve = makeDistortionCurve(Math.max(0, Math.min(1, +p.drive)));
    // tone 0..1 maps to a 500 Hz .. 12 kHz lowpass after the shaper.
    const freq = 500 + Math.max(0, Math.min(1, +p.tone)) * 11500;
    distortionTone.frequency.setTargetAtTime(freq, ctx.currentTime, 0.01);
  }

  /** Toggle the whole effects chain (crossfades bypass vs chain inputs). */
  static setEffectsEnabled(enabled) {
    if (!ctx) return;
    const t = ctx.currentTime;
    fxInputGain.gain.setTargetAtTime(enabled ? 1 : 0, t, 0.01);
    fxBypassGain.gain.setTargetAtTime(enabled ? 0 : 1, t, 0.01);
  }

  static setMasterVolume(volume) {
    if (!ctx) return;
    masterGain.gain.setTargetAtTime(Math.max(0, Math.min(1, +volume)), ctx.currentTime, 0.01);
  }

  static setSynthLayerVolume(volume) {
    if (!ctx) return;
    synthLayerGain.gain.setTargetAtTime(Math.max(0, Math.min(1, +volume)), ctx.currentTime, 0.01);
  }

  static setSampleLayerVolume(volume) {
    if (!ctx) return;
    sampleLayerGain.gain.setTargetAtTime(Math.max(0, Math.min(1, +volume)), ctx.currentTime, 0.01);
  }

  static setPolyphonyLimit(limit) {
    params.polyphonyLimit = Math.max(1, Math.min(32, limit | 0));
  }
}
