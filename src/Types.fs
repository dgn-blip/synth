module Synth.Types

open Browser.Types

// ---------------------------------------------------------------------------
// Domain types
// ---------------------------------------------------------------------------

/// Waveform types supported by the Web Audio OscillatorNode.
type WaveformType =
    | Sine
    | Square
    | Sawtooth
    | Triangle

/// ADSR envelope. Attack/decay/release are in seconds (0.0 - 5.0),
/// sustain is a level (0.0 - 1.0).
type ADSREnvelope =
    { attack: float
      decay: float
      sustain: float
      release: float }

/// Configuration for a single oscillator (the synth has three).
type OscillatorParams =
    { enabled: bool
      waveformType: WaveformType
      coarseTune: int     // -24 to +24 semitones
      fineTune: float     // -0.5 to +0.5 semitones
      volume: float }     // 0.0 to 1.0

/// Biquad filter types exposed in the UI.
type FilterType =
    | Lowpass
    | Highpass
    | Bandpass

/// Filter section with its own envelope modulation.
type FilterParams =
    { filterType: FilterType
      cutoff: float          // 20 Hz to 20 kHz (rendered logarithmically)
      resonance: float       // 0 to 20 (Q factor)
      envelope: ADSREnvelope
      envelopeAmount: float } // 0 to 1.0 (modulation depth)

type ReverbParams =
    { roomSize: float   // 0.0 to 1.0
      wetDry: float }   // 0 (dry) to 1 (wet)

type DelayParams =
    { delayTime: float  // 0.1 to 2.0 seconds
      feedback: float   // 0 to 0.9
      wetDry: float }   // 0 to 1.0

type DistortionParams =
    { drive: float      // 0 to 1.0
      tone: float }     // 0 to 1.0

type EffectsParams =
    { enabled: bool
      reverb: ReverbParams
      delay: DelayParams
      distortion: DistortionParams }

/// How an uploaded sample is triggered.
type SampleMode =
    | Pitched   // sample is pitch-shifted across the whole keyboard
    | Mapped    // sample only plays on explicitly mapped notes

/// An uploaded sample. The AudioBuffer lives in the JS audio engine and is
/// NOT serialized into presets (it only exists for the current session).
type SampleData =
    { id: string
      name: string
      audioBuffer: obj                 // Web Audio AudioBuffer (opaque to F#)
      mode: SampleMode
      volume: float                    // 0.0 to 1.0
      mappings: Map<string, string> }  // MIDI note (as string) -> sample id

/// Complete synthesizer state (everything that is audible).
type SynthState =
    { oscillators: OscillatorParams list
      envelope: ADSREnvelope
      filter: FilterParams
      effects: EffectsParams
      master: {| volume: float; polyphonyLimit: int |}
      samples: SampleData list
      sampleVolume: float
      synthVolume: float }

/// A saved preset. The synth state is stored pre-serialized so presets stay
/// valid even if in-memory sample buffers are gone.
type Preset =
    { name: string
      timestamp: System.DateTime
      stateJson: string }

/// A connected MIDI input device.
type MidiDevice =
    { id: string
      name: string }

/// The seven main tabs of the UI.
type Tab =
    | Dashboard
    | Oscillator
    | Filter
    | Envelope
    | Effects
    | Samples
    | Presets

/// Main application model.
type Model =
    { currentTab: Tab
      synth: SynthState
      presets: Preset list
      currentPresetName: string option
      midiConnected: bool
      midiDevices: MidiDevice list
      selectedMidiDevice: MidiDevice option
      errorMessage: string option
      successMessage: string option
      isInitialized: bool }

// ---------------------------------------------------------------------------
// Messages
// ---------------------------------------------------------------------------

type Msg =
    // Initialization
    | InitializeAudioContext
    | AudioContextReady
    | InitializeMidi
    | MidiReady of MidiDevice list
    | MidiInitFailed of string

    // Tab Navigation
    | SelectTab of Tab

    // MIDI Input
    | MidiNoteOn of note: int * velocity: float
    | MidiNoteOff of note: int
    | SelectMidiDevice of MidiDevice

    // Oscillator Controls (3 oscillators, indexed 0-2)
    | SetOscillatorEnabled of index: int * enabled: bool
    | SetOscillatorWaveform of index: int * WaveformType
    | SetOscillatorCoarseTune of index: int * semitones: int
    | SetOscillatorFineTune of index: int * cents: float
    | SetOscillatorVolume of index: int * volume: float

    // Filter Controls
    | SetFilterType of FilterType
    | SetFilterCutoff of frequency: float
    | SetFilterResonance of q: float
    | SetFilterEnvelopeAttack of seconds: float
    | SetFilterEnvelopeDecay of seconds: float
    | SetFilterEnvelopeSustain of level: float
    | SetFilterEnvelopeRelease of seconds: float
    | SetFilterEnvelopeAmount of amount: float

    // Amplitude Envelope Controls
    | SetEnvelopeAttack of seconds: float
    | SetEnvelopeDecay of seconds: float
    | SetEnvelopeSustain of level: float
    | SetEnvelopeRelease of seconds: float

    // Effects Controls
    | SetEffectsEnabled of enabled: bool
    | SetReverbRoomSize of size: float
    | SetReverbWetDry of mix: float
    | SetDelayTime of seconds: float
    | SetDelayFeedback of feedback: float
    | SetDelayWetDry of mix: float
    | SetDistortionDrive of drive: float
    | SetDistortionTone of tone: float

    // Volume & Mixing
    | SetMasterVolume of volume: float
    | SetSampleVolume of volume: float
    | SetSynthVolume of volume: float
    | SetPolyphonyLimit of limit: int

    // Sample Management
    | SampleFileSelected of File
    | SampleLoaded of id: string * name: string * buffer: obj
    | SampleLoadFailed of string
    | RemoveSample of sampleId: string
    | SetSampleMode of sampleId: string * SampleMode
    | SetSampleNote of sampleId: string * note: string
    | SetSampleVolumeFor of sampleId: string * volume: float

    // Preset Management
    | SavePreset of name: string
    | LoadPreset of presetName: string
    | DeletePreset of presetName: string
    | RenamePreset of oldName: string * newName: string
    | ExportPresets
    | ImportPresetsFile of File
    | PresetsImported of Preset list
    | PresetOperationFailed of string

    // UI State
    | SetError of message: string
    | ClearError
    | SetSuccess of message: string
    | ClearSuccess

// ---------------------------------------------------------------------------
// Helpers shared across modules
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module WaveformType =
    /// Web Audio OscillatorNode type string.
    let toJs (w: WaveformType) : string =
        match w with
        | Sine -> "sine"
        | Square -> "square"
        | Sawtooth -> "sawtooth"
        | Triangle -> "triangle"

    let toLabel (w: WaveformType) : string =
        match w with
        | Sine -> "Sine"
        | Square -> "Square"
        | Sawtooth -> "Sawtooth"
        | Triangle -> "Triangle"

    let ofString (s: string) : WaveformType =
        match s.ToLowerInvariant() with
        | "square" -> Square
        | "sawtooth" -> Sawtooth
        | "triangle" -> Triangle
        | _ -> Sine

    let all : WaveformType list = [ Sine; Square; Sawtooth; Triangle ]

[<RequireQualifiedAccess>]
module FilterType =
    /// Web Audio BiquadFilterNode type string.
    let toJs (f: FilterType) : string =
        match f with
        | Lowpass -> "lowpass"
        | Highpass -> "highpass"
        | Bandpass -> "bandpass"

    let toLabel (f: FilterType) : string =
        match f with
        | Lowpass -> "Lowpass"
        | Highpass -> "Highpass"
        | Bandpass -> "Bandpass"

    let ofString (s: string) : FilterType =
        match s.ToLowerInvariant() with
        | "highpass" -> Highpass
        | "bandpass" -> Bandpass
        | _ -> Lowpass

    let all : FilterType list = [ Lowpass; Highpass; Bandpass ]

[<RequireQualifiedAccess>]
module SampleMode =
    let toLabel (m: SampleMode) : string =
        match m with
        | Pitched -> "Pitched"
        | Mapped -> "Mapped"

    let ofString (s: string) : SampleMode =
        match s.ToLowerInvariant() with
        | "mapped" -> Mapped
        | _ -> Pitched

[<RequireQualifiedAccess>]
module MidiNotes =
    let private names =
        [| "C"; "C#"; "D"; "D#"; "E"; "F"; "F#"; "G"; "G#"; "A"; "A#"; "B" |]

    /// Human readable name (e.g. 60 -> "C4").
    let toName (midiNote: int) : string =
        let octave = midiNote / 12 - 1
        let name = names.[((midiNote % 12) + 12) % 12]
        sprintf "%s%d" name octave

    /// All notes selectable in the sample-mapping UI (C1..C7).
    let mappable : (int * string) list =
        [ for n in 24 .. 96 -> n, toName n ]

/// Clamp a float to an inclusive range. Used to validate every parameter
/// before it reaches the audio engine.
let clamp (lo: float) (hi: float) (v: float) : float =
    max lo (min hi v)

let clampInt (lo: int) (hi: int) (v: int) : int =
    max lo (min hi v)

// ---------------------------------------------------------------------------
// Defaults
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Defaults =
    let envelope : ADSREnvelope =
        { attack = 0.02; decay = 0.15; sustain = 0.7; release = 0.3 }

    let oscillator (enabled: bool) : OscillatorParams =
        { enabled = enabled
          waveformType = Sawtooth
          coarseTune = 0
          fineTune = 0.0
          volume = 0.6 }

    let filter : FilterParams =
        { filterType = Lowpass
          cutoff = 12000.0
          resonance = 0.7
          envelope = { attack = 0.05; decay = 0.3; sustain = 0.5; release = 0.4 }
          envelopeAmount = 0.0 }

    let effects : EffectsParams =
        { enabled = true
          reverb = { roomSize = 0.3; wetDry = 0.15 }
          delay = { delayTime = 0.35; feedback = 0.3; wetDry = 0.0 }
          distortion = { drive = 0.0; tone = 0.7 } }

    let synthState : SynthState =
        { oscillators =
            [ oscillator true
              oscillator false
              oscillator false ]
          envelope = envelope
          filter = filter
          effects = effects
          master = {| volume = 0.8; polyphonyLimit = 16 |}
          samples = []
          sampleVolume = 0.8
          synthVolume = 0.8 }
