module Synth.App

open System
open Browser.Dom
open Elmish
open Feliz
open Feliz.UseElmish
open Synth.Types
open Synth.Services
open Synth.Components

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------

let init () : Model * Cmd<Msg> =
    // Load stored presets up front; a corrupted store becomes a visible error
    // instead of a crash.
    let presets, loadError =
        match PresetManager.loadPresets () with
        | Ok presets -> presets, None
        | Error err -> [], Some err

    let model =
        { currentTab = Dashboard
          synth = Defaults.synthState
          presets = presets
          currentPresetName = None
          midiConnected = false
          midiDevices = []
          selectedMidiDevice = None
          errorMessage = loadError
          successMessage = None
          isInitialized = false }

    model, Cmd.ofMsg InitializeAudioContext

// ---------------------------------------------------------------------------
// Update helpers
// ---------------------------------------------------------------------------

/// Dispatch a message after a delay (used to auto-clear toasts).
let private delayed (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        window.setTimeout ((fun () -> dispatch msg), ms) |> ignore)

let private withSuccess (text: string) (model: Model) : Model * Cmd<Msg> =
    { model with successMessage = Some text; errorMessage = None },
    delayed 3500 ClearSuccess

let private withError (text: string) (model: Model) : Model * Cmd<Msg> =
    { model with errorMessage = Some text; successMessage = None },
    delayed 6000 ClearError

/// Apply a transformation to the synth state.
let private updateSynth (f: SynthState -> SynthState) (model: Model) : Model =
    { model with synth = f model.synth }

/// Update one oscillator slot by index, then push it to the audio engine.
let private updateOscillator (index: int) (f: OscillatorParams -> OscillatorParams) (model: Model) : Model =
    let oscillators =
        model.synth.oscillators
        |> List.mapi (fun i osc -> if i = index then f osc else osc)

    match List.tryItem index oscillators with
    | Some osc -> AudioEngine.syncOscillator index osc
    | None -> ()

    updateSynth (fun s -> { s with oscillators = oscillators }) model

/// Update the filter, then push it to the audio engine.
let private updateFilter (f: FilterParams -> FilterParams) (model: Model) : Model =
    let model = updateSynth (fun s -> { s with filter = f s.filter }) model
    AudioEngine.syncFilter model.synth.filter
    model

/// Update the amplitude envelope, then push it to the audio engine.
let private updateEnvelope (f: ADSREnvelope -> ADSREnvelope) (model: Model) : Model =
    let model = updateSynth (fun s -> { s with envelope = f s.envelope }) model
    AudioEngine.syncEnvelope model.synth.envelope
    model

/// Update the effects section, then push it to the audio engine.
let private updateEffects (f: EffectsParams -> EffectsParams) (model: Model) : Model =
    let model = updateSynth (fun s -> { s with effects = f s.effects }) model
    AudioEngine.syncEffects model.synth.effects
    model

/// Update one sample by id.
let private updateSample (sampleId: string) (f: SampleData -> SampleData) (model: Model) : Model =
    updateSynth
        (fun s ->
            { s with
                samples =
                    s.samples
                    |> List.map (fun smp -> if smp.id = sampleId then f smp else smp) })
        model

/// Persist the preset list, mapping storage failures to an error message.
let private persistPresets (presets: Preset list) (model: Model) (successText: string) : Model * Cmd<Msg> =
    match PresetManager.savePresets presets with
    | Ok() -> withSuccess successText { model with presets = presets }
    | Error err -> withError err model

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with

    // --- Initialization ----------------------------------------------------
    | InitializeAudioContext ->
        if AudioEngine.initialize () then
            AudioEngine.syncAll model.synth
            model, Cmd.ofMsg AudioContextReady
        else
            withError "Web Audio is not supported in this browser. Try Chrome, Edge or Firefox." model

    | AudioContextReady ->
        { model with isInitialized = true }, Cmd.ofMsg InitializeMidi

    | InitializeMidi ->
        model,
        Cmd.OfPromise.either
            (fun () -> MidiInput.requestAccess ())
            ()
            (fun _ -> MidiReady(MidiInput.listDevices ()))
            (fun ex -> MidiInitFailed ex.Message)

    | MidiReady devices ->
        let model =
            { model with
                midiDevices = devices
                midiConnected = not devices.IsEmpty }
        // Auto-select the first device so a plugged-in keyboard just works.
        match devices with
        | first :: _ when model.selectedMidiDevice.IsNone ->
            model, Cmd.ofMsg (SelectMidiDevice first)
        | _ -> model, Cmd.none

    | MidiInitFailed err ->
        withError err { model with midiConnected = false }

    // --- Tab navigation ----------------------------------------------------
    | SelectTab tab ->
        { model with currentTab = tab }, Cmd.none

    // --- MIDI input ----------------------------------------------------------
    | SelectMidiDevice device ->
        { model with
            selectedMidiDevice = Some device
            midiConnected = true },
        Cmd.ofEffect (fun dispatch ->
            if not (MidiInput.subscribe device.id dispatch) then
                dispatch (SetError(sprintf "Could not open MIDI device '%s'." device.name)))

    | MidiNoteOn(note, velocity) ->
        AudioEngine.noteOn note velocity
        model, Cmd.none

    | MidiNoteOff note ->
        AudioEngine.noteOff note
        model, Cmd.none

    // --- Oscillators ---------------------------------------------------------
    | SetOscillatorEnabled(i, enabled) ->
        updateOscillator i (fun o -> { o with enabled = enabled }) model, Cmd.none

    | SetOscillatorWaveform(i, wave) ->
        updateOscillator i (fun o -> { o with waveformType = wave }) model, Cmd.none

    | SetOscillatorCoarseTune(i, semis) ->
        updateOscillator i (fun o -> { o with coarseTune = clampInt -24 24 semis }) model, Cmd.none

    | SetOscillatorFineTune(i, fine) ->
        updateOscillator i (fun o -> { o with fineTune = clamp -0.5 0.5 fine }) model, Cmd.none

    | SetOscillatorVolume(i, vol) ->
        updateOscillator i (fun o -> { o with volume = clamp 0.0 1.0 vol }) model, Cmd.none

    // --- Filter ----------------------------------------------------------------
    | SetFilterType ft ->
        updateFilter (fun f -> { f with filterType = ft }) model, Cmd.none

    | SetFilterCutoff freq ->
        updateFilter (fun f -> { f with cutoff = clamp 20.0 20000.0 freq }) model, Cmd.none

    | SetFilterResonance q ->
        updateFilter (fun f -> { f with resonance = clamp 0.0 20.0 q }) model, Cmd.none

    | SetFilterEnvelopeAttack s ->
        updateFilter (fun f -> { f with envelope = { f.envelope with attack = clamp 0.0 5.0 s } }) model, Cmd.none

    | SetFilterEnvelopeDecay s ->
        updateFilter (fun f -> { f with envelope = { f.envelope with decay = clamp 0.0 5.0 s } }) model, Cmd.none

    | SetFilterEnvelopeSustain lvl ->
        updateFilter (fun f -> { f with envelope = { f.envelope with sustain = clamp 0.0 1.0 lvl } }) model, Cmd.none

    | SetFilterEnvelopeRelease s ->
        updateFilter (fun f -> { f with envelope = { f.envelope with release = clamp 0.0 5.0 s } }) model, Cmd.none

    | SetFilterEnvelopeAmount amount ->
        updateFilter (fun f -> { f with envelopeAmount = clamp 0.0 1.0 amount }) model, Cmd.none

    // --- Amplitude envelope ------------------------------------------------------
    | SetEnvelopeAttack s ->
        updateEnvelope (fun e -> { e with attack = clamp 0.0 5.0 s }) model, Cmd.none

    | SetEnvelopeDecay s ->
        updateEnvelope (fun e -> { e with decay = clamp 0.0 5.0 s }) model, Cmd.none

    | SetEnvelopeSustain lvl ->
        updateEnvelope (fun e -> { e with sustain = clamp 0.0 1.0 lvl }) model, Cmd.none

    | SetEnvelopeRelease s ->
        updateEnvelope (fun e -> { e with release = clamp 0.0 5.0 s }) model, Cmd.none

    // --- Effects --------------------------------------------------------------
    | SetEffectsEnabled enabled ->
        updateEffects (fun fx -> { fx with enabled = enabled }) model, Cmd.none

    | SetReverbRoomSize size ->
        updateEffects (fun fx -> { fx with reverb = { fx.reverb with roomSize = clamp 0.0 1.0 size } }) model, Cmd.none

    | SetReverbWetDry mix ->
        updateEffects (fun fx -> { fx with reverb = { fx.reverb with wetDry = clamp 0.0 1.0 mix } }) model, Cmd.none

    | SetDelayTime s ->
        updateEffects (fun fx -> { fx with delay = { fx.delay with delayTime = clamp 0.1 2.0 s } }) model, Cmd.none

    | SetDelayFeedback fb ->
        updateEffects (fun fx -> { fx with delay = { fx.delay with feedback = clamp 0.0 0.9 fb } }) model, Cmd.none

    | SetDelayWetDry mix ->
        updateEffects (fun fx -> { fx with delay = { fx.delay with wetDry = clamp 0.0 1.0 mix } }) model, Cmd.none

    | SetDistortionDrive drive ->
        updateEffects (fun fx -> { fx with distortion = { fx.distortion with drive = clamp 0.0 1.0 drive } }) model, Cmd.none

    | SetDistortionTone tone ->
        updateEffects (fun fx -> { fx with distortion = { fx.distortion with tone = clamp 0.0 1.0 tone } }) model, Cmd.none

    // --- Volume & mixing ----------------------------------------------------------
    | SetMasterVolume vol ->
        let vol = clamp 0.0 1.0 vol
        AudioEngine.setMasterVolume vol
        updateSynth (fun s -> { s with master = {| s.master with volume = vol |} }) model, Cmd.none

    | SetSampleVolume vol ->
        let vol = clamp 0.0 1.0 vol
        AudioEngine.setSampleLayerVolume vol
        updateSynth (fun s -> { s with sampleVolume = vol }) model, Cmd.none

    | SetSynthVolume vol ->
        let vol = clamp 0.0 1.0 vol
        AudioEngine.setSynthLayerVolume vol
        updateSynth (fun s -> { s with synthVolume = vol }) model, Cmd.none

    | SetPolyphonyLimit limit ->
        let limit = clampInt 1 32 limit
        AudioEngine.setPolyphonyLimit limit
        updateSynth (fun s -> { s with master = {| s.master with polyphonyLimit = limit |} }) model, Cmd.none

    // --- Samples ---------------------------------------------------------------
    | SampleFileSelected file ->
        let sampleId = Guid.NewGuid().ToString("N")
        model,
        Cmd.OfPromise.either
            (fun () -> AudioEngine.loadSampleFromFile file)
            ()
            (fun buffer -> SampleLoaded(sampleId, file.name, buffer))
            (fun ex -> SampleLoadFailed ex.Message)

    | SampleLoaded(id, name, buffer) ->
        let sample =
            { id = id
              name = name
              audioBuffer = buffer
              mode = Pitched
              volume = 0.8
              mappings = Map.empty }
        AudioEngine.registerSample id buffer Pitched sample.volume
        updateSynth (fun s -> { s with samples = s.samples @ [ sample ] }) model
        |> withSuccess (sprintf "Sample '%s' loaded." name)

    | SampleLoadFailed err ->
        withError err model

    | RemoveSample sampleId ->
        AudioEngine.removeSample sampleId
        updateSynth (fun s -> { s with samples = s.samples |> List.filter (fun smp -> smp.id <> sampleId) }) model,
        Cmd.none

    | SetSampleMode(sampleId, mode) ->
        AudioEngine.setSampleMode sampleId mode
        updateSample sampleId (fun smp -> { smp with mode = mode }) model, Cmd.none

    | SetSampleNote(sampleId, note) ->
        match Int32.TryParse note with
        | true, midiNote ->
            AudioEngine.setSampleNoteMapping sampleId midiNote
            // The mapping is stored as MIDI-note-string -> sample id.
            updateSample sampleId (fun smp -> { smp with mappings = Map.ofList [ note, sampleId ] }) model,
            Cmd.none
        | _ ->
            withError (sprintf "'%s' is not a valid MIDI note." note) model

    | SetSampleVolumeFor(sampleId, vol) ->
        let vol = clamp 0.0 1.0 vol
        AudioEngine.setSamplePlaybackVolume sampleId vol
        updateSample sampleId (fun smp -> { smp with volume = vol }) model, Cmd.none

    // --- Presets ----------------------------------------------------------------
    | SavePreset name ->
        let preset =
            { name = name
              timestamp = DateTime.Now
              stateJson = PresetManager.encodeSynthState model.synth }
        // Saving under an existing name overwrites that preset.
        let presets = (model.presets |> List.filter (fun p -> p.name <> name)) @ [ preset ]
        let model = { model with currentPresetName = Some name }
        persistPresets presets model (sprintf "Preset '%s' saved." name)

    | LoadPreset name ->
        match model.presets |> List.tryFind (fun p -> p.name = name) with
        | None -> withError (sprintf "Preset '%s' was not found." name) model
        | Some preset ->
            match PresetManager.parseStateJson preset.stateJson with
            | Error err -> withError (sprintf "Preset '%s' is corrupted: %s" name err) model
            | Ok loadedState ->
                // Keep live audio buffers for samples that still exist in
                // memory (same id), so loading a preset mid-session keeps sound.
                let samples =
                    loadedState.samples
                    |> List.map (fun smp ->
                        match model.synth.samples |> List.tryFind (fun cur -> cur.id = smp.id) with
                        | Some current when not (isNull current.audioBuffer) ->
                            { smp with audioBuffer = current.audioBuffer }
                        | _ -> smp)

                let newState = { loadedState with samples = samples }
                AudioEngine.allNotesOff ()
                AudioEngine.syncAll newState

                { model with
                    synth = newState
                    currentPresetName = Some name }
                |> withSuccess (sprintf "Preset '%s' loaded." name)

    | DeletePreset name ->
        let presets = model.presets |> List.filter (fun p -> p.name <> name)
        let model =
            if model.currentPresetName = Some name then
                { model with currentPresetName = None }
            else
                model
        persistPresets presets model (sprintf "Preset '%s' deleted." name)

    | RenamePreset(oldName, newName) ->
        if model.presets |> List.exists (fun p -> p.name = newName) then
            withError (sprintf "A preset named '%s' already exists." newName) model
        else
            let presets =
                model.presets
                |> List.map (fun p -> if p.name = oldName then { p with name = newName } else p)
            let model =
                if model.currentPresetName = Some oldName then
                    { model with currentPresetName = Some newName }
                else
                    model
            persistPresets presets model (sprintf "Preset renamed to '%s'." newName)

    | ExportPresets ->
        if model.presets.IsEmpty then
            withError "There are no presets to export." model
        else
            PresetManager.exportToFile model.presets
            withSuccess "Presets exported as fable-synth-presets.json." model

    | ImportPresetsFile file ->
        model,
        Cmd.ofEffect (fun dispatch ->
            PresetManager.importFromFile
                file
                (PresetsImported >> dispatch)
                (PresetOperationFailed >> dispatch))

    | PresetsImported imported ->
        // Imported presets overwrite same-named existing ones.
        let importedNames = imported |> List.map (fun p -> p.name) |> Set.ofList
        let presets =
            (model.presets |> List.filter (fun p -> not (importedNames.Contains p.name))) @ imported
        persistPresets presets model (sprintf "%d preset(s) imported." imported.Length)

    | PresetOperationFailed err ->
        withError err model

    // --- UI state -----------------------------------------------------------------
    | SetError text -> withError text model
    | ClearError -> { model with errorMessage = None }, Cmd.none
    | SetSuccess text -> withSuccess text model
    | ClearSuccess -> { model with successMessage = None }, Cmd.none

// ---------------------------------------------------------------------------
// View
// ---------------------------------------------------------------------------

let private tabs : (Tab * string) list =
    [ Dashboard, "Dashboard"
      Oscillator, "Oscillator"
      Filter, "Filter"
      Envelope, "Envelope"
      Effects, "Effects"
      Samples, "Samples"
      Presets, "Presets" ]

let private tabBar (current: Tab) (dispatch: Msg -> unit) : ReactElement =
    Html.nav [
        prop.className "tab-bar"
        prop.role "tablist"
        prop.children [
            for tab, label in tabs ->
                // Bind first: passing `tab = current` inline would be parsed
                // as a named method argument, not an equality test.
                let isActive = tab = current
                Html.button [
                    prop.className (if isActive then "tab tab-active" else "tab")
                    prop.role "tab"
                    prop.ariaSelected isActive
                    prop.text label
                    prop.onClick (fun _ -> dispatch (SelectTab tab))
                ]
        ]
    ]

/// Toast banners for transient success / error messages.
let private toasts (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "toast-area"
        prop.children [
            match model.errorMessage with
            | Some text ->
                Html.div [
                    prop.className "toast toast-error"
                    prop.role "alert"
                    prop.children [
                        Html.span [ prop.text text ]
                        Html.button [
                            prop.className "toast-close"
                            prop.ariaLabel "Dismiss error"
                            prop.text "×"
                            prop.onClick (fun _ -> dispatch ClearError)
                        ]
                    ]
                ]
            | None -> Html.none

            match model.successMessage with
            | Some text ->
                Html.div [
                    prop.className "toast toast-success"
                    prop.role "status"
                    prop.children [ Html.span [ prop.text text ] ]
                ]
            | None -> Html.none
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [
                prop.className "app-header"
                prop.children [
                    Html.div [
                        prop.className "app-title"
                        prop.children [
                            Html.h1 [ prop.text "Fable Synth" ]
                            Html.span [ prop.className "app-subtitle"; prop.text "Browser synthesizer" ]
                        ]
                    ]
                    Html.div [
                        prop.className "header-status"
                        prop.children [
                            Common.statusBadge
                                (if model.midiConnected then "MIDI" else "No MIDI")
                                model.midiConnected
                        ]
                    ]
                ]
            ]

            tabBar model.currentTab dispatch

            Html.main [
                prop.className "tab-content"
                prop.role "tabpanel"
                prop.children [
                    match model.currentTab with
                    | Dashboard -> DashboardTab.view model dispatch
                    | Oscillator -> OscillatorTab.view model dispatch
                    | Filter -> FilterTab.view model dispatch
                    | Envelope -> EnvelopeTab.view model dispatch
                    | Effects -> EffectsTab.view model dispatch
                    | Samples -> SamplesTab.view model dispatch
                    | Presets -> PresetsTab.View model dispatch
                ]
            ]

            toasts model dispatch
        ]
    ]

// ---------------------------------------------------------------------------
// Entry point (React 18 root + useElmish hook)
// ---------------------------------------------------------------------------

[<ReactComponent>]
let App () : ReactElement =
    let model, dispatch = React.useElmish (init, update, [||])
    view model dispatch

let root = ReactDOM.createRoot (document.getElementById "root")
root.render (App())
