module Synth.Services.PresetManager

open System
open Browser
open Browser.Types
open Thoth.Json
open Synth.Types

/// localStorage key under which the preset list is stored.
[<Literal>]
let private StorageKey = "fable-synth-presets"

// ---------------------------------------------------------------------------
// Encoders — AudioBuffers are intentionally NOT serialized. Samples keep
// their metadata (name, mode, volume, mappings) so a loaded preset shows
// which samples to re-upload.
// ---------------------------------------------------------------------------

let private encodeEnvelope (env: ADSREnvelope) : JsonValue =
    Encode.object
        [ "attack", Encode.float env.attack
          "decay", Encode.float env.decay
          "sustain", Encode.float env.sustain
          "release", Encode.float env.release ]

let private encodeOscillator (osc: OscillatorParams) : JsonValue =
    Encode.object
        [ "enabled", Encode.bool osc.enabled
          "waveformType", Encode.string (WaveformType.toJs osc.waveformType)
          "coarseTune", Encode.int osc.coarseTune
          "fineTune", Encode.float osc.fineTune
          "volume", Encode.float osc.volume ]

let private encodeFilter (f: FilterParams) : JsonValue =
    Encode.object
        [ "filterType", Encode.string (FilterType.toJs f.filterType)
          "cutoff", Encode.float f.cutoff
          "resonance", Encode.float f.resonance
          "envelope", encodeEnvelope f.envelope
          "envelopeAmount", Encode.float f.envelopeAmount ]

let private encodeEffects (fx: EffectsParams) : JsonValue =
    Encode.object
        [ "enabled", Encode.bool fx.enabled
          "reverb",
          Encode.object
              [ "roomSize", Encode.float fx.reverb.roomSize
                "wetDry", Encode.float fx.reverb.wetDry ]
          "delay",
          Encode.object
              [ "delayTime", Encode.float fx.delay.delayTime
                "feedback", Encode.float fx.delay.feedback
                "wetDry", Encode.float fx.delay.wetDry ]
          "distortion",
          Encode.object
              [ "drive", Encode.float fx.distortion.drive
                "tone", Encode.float fx.distortion.tone ] ]

let private encodeSample (s: SampleData) : JsonValue =
    Encode.object
        [ "id", Encode.string s.id
          "name", Encode.string s.name
          // audioBuffer is deliberately omitted — it cannot be serialized.
          "mode", Encode.string ((SampleMode.toLabel s.mode).ToLowerInvariant())
          "volume", Encode.float s.volume
          "mappings",
          Encode.object
              [ for KeyValue(note, sampleId) in s.mappings -> note, Encode.string sampleId ] ]

let encodeSynthState (state: SynthState) : string =
    Encode.object
        [ "oscillators", Encode.list (state.oscillators |> List.map encodeOscillator)
          "envelope", encodeEnvelope state.envelope
          "filter", encodeFilter state.filter
          "effects", encodeEffects state.effects
          "masterVolume", Encode.float state.master.volume
          "polyphonyLimit", Encode.int state.master.polyphonyLimit
          "samples", Encode.list (state.samples |> List.map encodeSample)
          "sampleVolume", Encode.float state.sampleVolume
          "synthVolume", Encode.float state.synthVolume ]
    |> Encode.toString 0

let private encodePreset (p: Preset) : JsonValue =
    Encode.object
        [ "name", Encode.string p.name
          "timestamp", Encode.string (p.timestamp.ToString("o"))
          "stateJson", Encode.string p.stateJson ]

let encodePresets (presets: Preset list) : string =
    Encode.list (presets |> List.map encodePreset) |> Encode.toString 2

// ---------------------------------------------------------------------------
// Decoders
// ---------------------------------------------------------------------------

let private decodeEnvelope: Decoder<ADSREnvelope> =
    Decode.object (fun get ->
        { attack = get.Required.Field "attack" Decode.float |> clamp 0.0 5.0
          decay = get.Required.Field "decay" Decode.float |> clamp 0.0 5.0
          sustain = get.Required.Field "sustain" Decode.float |> clamp 0.0 1.0
          release = get.Required.Field "release" Decode.float |> clamp 0.0 5.0 })

let private decodeOscillator: Decoder<OscillatorParams> =
    Decode.object (fun get ->
        { enabled = get.Required.Field "enabled" Decode.bool
          waveformType = get.Required.Field "waveformType" Decode.string |> WaveformType.ofString
          coarseTune = get.Required.Field "coarseTune" Decode.int |> clampInt -24 24
          fineTune = get.Required.Field "fineTune" Decode.float |> clamp -0.5 0.5
          volume = get.Required.Field "volume" Decode.float |> clamp 0.0 1.0 })

let private decodeFilter: Decoder<FilterParams> =
    Decode.object (fun get ->
        { filterType = get.Required.Field "filterType" Decode.string |> FilterType.ofString
          cutoff = get.Required.Field "cutoff" Decode.float |> clamp 20.0 20000.0
          resonance = get.Required.Field "resonance" Decode.float |> clamp 0.0 20.0
          envelope = get.Required.Field "envelope" decodeEnvelope
          envelopeAmount = get.Required.Field "envelopeAmount" Decode.float |> clamp 0.0 1.0 })

let private decodeEffects: Decoder<EffectsParams> =
    Decode.object (fun get ->
        { enabled = get.Required.Field "enabled" Decode.bool
          reverb =
            get.Required.Field
                "reverb"
                (Decode.object (fun g ->
                    { roomSize = g.Required.Field "roomSize" Decode.float |> clamp 0.0 1.0
                      wetDry = g.Required.Field "wetDry" Decode.float |> clamp 0.0 1.0 }))
          delay =
            get.Required.Field
                "delay"
                (Decode.object (fun g ->
                    { delayTime = g.Required.Field "delayTime" Decode.float |> clamp 0.1 2.0
                      feedback = g.Required.Field "feedback" Decode.float |> clamp 0.0 0.9
                      wetDry = g.Required.Field "wetDry" Decode.float |> clamp 0.0 1.0 }))
          distortion =
            get.Required.Field
                "distortion"
                (Decode.object (fun g ->
                    { drive = g.Required.Field "drive" Decode.float |> clamp 0.0 1.0
                      tone = g.Required.Field "tone" Decode.float |> clamp 0.0 1.0 })) })

let private decodeSample: Decoder<SampleData> =
    Decode.object (fun get ->
        { id = get.Required.Field "id" Decode.string
          name = get.Required.Field "name" Decode.string
          // The buffer never round-trips through JSON; the user re-uploads.
          audioBuffer = null
          mode = get.Required.Field "mode" Decode.string |> SampleMode.ofString
          volume = get.Required.Field "volume" Decode.float |> clamp 0.0 1.0
          // Decode.dict already produces a Map<string, string>.
          mappings =
            get.Optional.Field "mappings" (Decode.dict Decode.string)
            |> Option.defaultValue Map.empty })

let decodeSynthState: Decoder<SynthState> =
    Decode.object (fun get ->
        { oscillators = get.Required.Field "oscillators" (Decode.list decodeOscillator)
          envelope = get.Required.Field "envelope" decodeEnvelope
          filter = get.Required.Field "filter" decodeFilter
          effects = get.Required.Field "effects" decodeEffects
          master =
            {| volume = get.Required.Field "masterVolume" Decode.float |> clamp 0.0 1.0
               polyphonyLimit = get.Required.Field "polyphonyLimit" Decode.int |> clampInt 1 32 |}
          samples =
            get.Optional.Field "samples" (Decode.list decodeSample)
            |> Option.defaultValue []
          sampleVolume = get.Required.Field "sampleVolume" Decode.float |> clamp 0.0 1.0
          synthVolume = get.Required.Field "synthVolume" Decode.float |> clamp 0.0 1.0 })

let private decodePreset: Decoder<Preset> =
    Decode.object (fun get ->
        { name = get.Required.Field "name" Decode.string
          timestamp =
            get.Required.Field "timestamp" Decode.string
            |> fun s ->
                match DateTime.TryParse s with
                | true, dt -> dt
                | false, _ -> DateTime.Now
          stateJson = get.Required.Field "stateJson" Decode.string })

let decodePresets (json: string) : Result<Preset list, string> =
    Decode.fromString (Decode.list decodePreset) json

/// Parse the serialized state stored inside a preset.
let parseStateJson (json: string) : Result<SynthState, string> =
    Decode.fromString decodeSynthState json

// ---------------------------------------------------------------------------
// localStorage persistence
// ---------------------------------------------------------------------------

/// Load all presets from localStorage. A corrupt store returns an Error so
/// the UI can tell the user instead of silently losing data.
let loadPresets () : Result<Preset list, string> =
    try
        match WebStorage.localStorage.getItem StorageKey with
        | null -> Ok []
        | json ->
            match decodePresets json with
            | Ok presets -> Ok presets
            | Error err -> Error(sprintf "Stored presets are corrupted: %s" err)
    with ex ->
        Error(sprintf "Could not read presets from browser storage: %s" ex.Message)

/// Persist all presets to localStorage. Handles quota errors gracefully.
let savePresets (presets: Preset list) : Result<unit, string> =
    try
        WebStorage.localStorage.setItem (StorageKey, encodePresets presets)
        Ok()
    with _ ->
        Error "Browser storage is full. Delete some presets or export them to a file."

// ---------------------------------------------------------------------------
// Export / import as JSON files
// ---------------------------------------------------------------------------

/// Trigger a download of all presets as a JSON file.
let exportToFile (presets: Preset list) : unit =
    let json = encodePresets presets
    let parts: obj [] = [| box json |]
    let options =
        Fable.Core.JsInterop.jsOptions<BlobPropertyBag> (fun o -> o.``type`` <- "application/json")
    let blob = Blob.Create(parts, options)
    let url = Url.URL.createObjectURL blob
    let anchor = document.createElement "a" :?> HTMLAnchorElement
    anchor.href <- url
    anchor.setAttribute ("download", "fable-synth-presets.json")
    document.body.appendChild anchor |> ignore
    anchor.click ()
    document.body.removeChild anchor |> ignore
    Url.URL.revokeObjectURL url

/// Read and decode a preset JSON file selected by the user.
/// Calls onSuccess/onError on the appropriate outcome.
let importFromFile
    (file: File)
    (onSuccess: Preset list -> unit)
    (onError: string -> unit)
    : unit =
    let reader = FileReader.Create()
    reader.onload <-
        fun _ ->
            let content = string reader.result
            match decodePresets content with
            | Ok presets -> onSuccess presets
            | Error err -> onError (sprintf "That file is not a valid preset file: %s" err)
    reader.onerror <- fun _ -> onError "Could not read the selected file."
    reader.readAsText file
