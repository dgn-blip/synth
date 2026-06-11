module Synth.Services.AudioEngine

open Fable.Core
open Browser.Types
open Synth.Types

// ---------------------------------------------------------------------------
// Interop surface of js/audioEngine.js
// ---------------------------------------------------------------------------

/// Shape of the static `AudioEngine` class exported by audioEngine.js.
/// Each abstract member maps 1:1 to a static method on the JS class.
type IAudioEngine =
    abstract createAudioContext: unit -> bool
    abstract resume: unit -> unit
    abstract disconnectAll: unit -> unit

    // Notes
    abstract noteOn: midiNote: int * velocity: float -> unit
    abstract noteOff: midiNote: int -> unit
    abstract allNotesOff: unit -> unit

    // Parameters
    abstract setOscillatorParams:
        index: int *
        p: {| enabled: bool; waveType: string; coarseTune: int; fineTune: float; volume: float |}
            -> unit

    abstract setAmplitudeEnvelope:
        env: {| attack: float; decay: float; sustain: float; release: float |} -> unit

    abstract setFilterParams: p: {| ``type``: string; cutoff: float; q: float |} -> unit

    abstract updateFilterEnvelope:
        env: {| attack: float; decay: float; sustain: float; release: float; amount: float |}
            -> unit

    abstract setReverbParams: p: {| roomSize: float; wetDry: float |} -> unit
    abstract setDelayParams: p: {| time: float; feedback: float; wetDry: float |} -> unit
    abstract setDistortionParams: p: {| drive: float; tone: float |} -> unit
    abstract setEffectsEnabled: enabled: bool -> unit

    abstract setMasterVolume: volume: float -> unit
    abstract setSynthLayerVolume: volume: float -> unit
    abstract setSampleLayerVolume: volume: float -> unit
    abstract setPolyphonyLimit: limit: int -> unit

    // Samples
    abstract loadSampleFromFile: file: File -> JS.Promise<obj>
    abstract registerSample: sampleId: string * buffer: obj * mode: string * volume: float -> unit
    abstract removeSample: sampleId: string -> unit
    abstract setSampleMode: sampleId: string * mode: string -> unit
    abstract setSamplePlaybackVolume: sampleId: string * volume: float -> unit
    abstract setSampleNoteMapping: sampleId: string * midiNote: int -> unit

/// The static AudioEngine class imported from the JS module.
[<ImportMember("../../js/audioEngine.js")>]
let AudioEngine: IAudioEngine = jsNative

// ---------------------------------------------------------------------------
// Typed helpers — translate F# domain types into the plain objects JS expects.
// These are the only functions the rest of the app calls.
// ---------------------------------------------------------------------------

/// Create the AudioContext and audio graph. Returns false if unsupported.
let initialize () : bool =
    AudioEngine.createAudioContext ()

/// Push the entire synth state into the JS engine. Used on startup and after
/// loading a preset, so the audible state always matches the model.
let syncAll (synth: SynthState) : unit =
    synth.oscillators
    |> List.iteri (fun i osc ->
        AudioEngine.setOscillatorParams (
            i,
            {| enabled = osc.enabled
               waveType = WaveformType.toJs osc.waveformType
               coarseTune = osc.coarseTune
               fineTune = osc.fineTune
               volume = osc.volume |}
        ))

    AudioEngine.setAmplitudeEnvelope
        {| attack = synth.envelope.attack
           decay = synth.envelope.decay
           sustain = synth.envelope.sustain
           release = synth.envelope.release |}

    AudioEngine.setFilterParams
        {| ``type`` = FilterType.toJs synth.filter.filterType
           cutoff = synth.filter.cutoff
           q = synth.filter.resonance |}

    AudioEngine.updateFilterEnvelope
        {| attack = synth.filter.envelope.attack
           decay = synth.filter.envelope.decay
           sustain = synth.filter.envelope.sustain
           release = synth.filter.envelope.release
           amount = synth.filter.envelopeAmount |}

    AudioEngine.setReverbParams
        {| roomSize = synth.effects.reverb.roomSize
           wetDry = synth.effects.reverb.wetDry |}

    AudioEngine.setDelayParams
        {| time = synth.effects.delay.delayTime
           feedback = synth.effects.delay.feedback
           wetDry = synth.effects.delay.wetDry |}

    AudioEngine.setDistortionParams
        {| drive = synth.effects.distortion.drive
           tone = synth.effects.distortion.tone |}

    AudioEngine.setEffectsEnabled synth.effects.enabled
    AudioEngine.setMasterVolume synth.master.volume
    AudioEngine.setSynthLayerVolume synth.synthVolume
    AudioEngine.setSampleLayerVolume synth.sampleVolume
    AudioEngine.setPolyphonyLimit synth.master.polyphonyLimit

    // Re-register any samples that still have a live buffer (e.g. after a
    // preset load within the same session).
    synth.samples
    |> List.iter (fun s ->
        if not (isNull s.audioBuffer) then
            AudioEngine.registerSample (
                s.id,
                s.audioBuffer,
                (SampleMode.toLabel s.mode).ToLowerInvariant(),
                s.volume
            )

            s.mappings
            |> Map.iter (fun note _ ->
                match System.Int32.TryParse note with
                | true, midiNote -> AudioEngine.setSampleNoteMapping (s.id, midiNote)
                | _ -> ()))

let syncOscillator (index: int) (osc: OscillatorParams) : unit =
    AudioEngine.setOscillatorParams (
        index,
        {| enabled = osc.enabled
           waveType = WaveformType.toJs osc.waveformType
           coarseTune = osc.coarseTune
           fineTune = osc.fineTune
           volume = osc.volume |}
    )

let syncEnvelope (env: ADSREnvelope) : unit =
    AudioEngine.setAmplitudeEnvelope
        {| attack = env.attack
           decay = env.decay
           sustain = env.sustain
           release = env.release |}

let syncFilter (filter: FilterParams) : unit =
    AudioEngine.setFilterParams
        {| ``type`` = FilterType.toJs filter.filterType
           cutoff = filter.cutoff
           q = filter.resonance |}

    AudioEngine.updateFilterEnvelope
        {| attack = filter.envelope.attack
           decay = filter.envelope.decay
           sustain = filter.envelope.sustain
           release = filter.envelope.release
           amount = filter.envelopeAmount |}

let syncEffects (fx: EffectsParams) : unit =
    AudioEngine.setReverbParams
        {| roomSize = fx.reverb.roomSize; wetDry = fx.reverb.wetDry |}

    AudioEngine.setDelayParams
        {| time = fx.delay.delayTime
           feedback = fx.delay.feedback
           wetDry = fx.delay.wetDry |}

    AudioEngine.setDistortionParams
        {| drive = fx.distortion.drive; tone = fx.distortion.tone |}

    AudioEngine.setEffectsEnabled fx.enabled

let noteOn (note: int) (velocity: float) : unit = AudioEngine.noteOn (note, velocity)
let noteOff (note: int) : unit = AudioEngine.noteOff note
let allNotesOff () : unit = AudioEngine.allNotesOff ()

let setMasterVolume (v: float) : unit = AudioEngine.setMasterVolume v
let setSynthLayerVolume (v: float) : unit = AudioEngine.setSynthLayerVolume v
let setSampleLayerVolume (v: float) : unit = AudioEngine.setSampleLayerVolume v
let setPolyphonyLimit (n: int) : unit = AudioEngine.setPolyphonyLimit n

let loadSampleFromFile (file: File) : JS.Promise<obj> =
    AudioEngine.loadSampleFromFile file

let registerSample (id: string) (buffer: obj) (mode: SampleMode) (volume: float) : unit =
    AudioEngine.registerSample (id, buffer, (SampleMode.toLabel mode).ToLowerInvariant(), volume)

let removeSample (id: string) : unit = AudioEngine.removeSample id

let setSampleMode (id: string) (mode: SampleMode) : unit =
    AudioEngine.setSampleMode (id, (SampleMode.toLabel mode).ToLowerInvariant())

let setSampleNoteMapping (id: string) (midiNote: int) : unit =
    AudioEngine.setSampleNoteMapping (id, midiNote)

let setSamplePlaybackVolume (id: string) (volume: float) : unit =
    AudioEngine.setSamplePlaybackVolume (id, volume)
