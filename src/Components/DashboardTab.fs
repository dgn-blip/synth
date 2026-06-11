module Synth.Components.DashboardTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// Dashboard: MIDI status, device picker, master controls and quick preset load.
let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "tab-grid"
        prop.children [

            card "Status" [
                Html.div [
                    prop.className "status-row"
                    prop.children [
                        statusBadge
                            (if model.isInitialized then "Audio engine ready" else "Audio engine off")
                            model.isInitialized
                        statusBadge
                            (if model.midiConnected then "MIDI connected" else "MIDI disconnected")
                            model.midiConnected
                    ]
                ]
                if not model.midiConnected then
                    Html.p [
                        prop.className "hint"
                        prop.text "Connect a USB MIDI keyboard and click Reconnect MIDI."
                    ]
                Html.div [
                    prop.className "button-row"
                    prop.children [
                        secondaryButton "Reconnect MIDI" (fun () -> dispatch InitializeMidi)
                    ]
                ]
            ]

            card "MIDI Device" [
                if model.midiDevices.IsEmpty then
                    Html.p [ prop.className "hint"; prop.text "No MIDI input devices found." ]
                else
                    selectBox
                        "Input device"
                        [ for d in model.midiDevices -> d.id, d.name ]
                        (model.selectedMidiDevice
                         |> Option.map (fun d -> d.id)
                         |> Option.defaultValue "")
                        (fun id ->
                            model.midiDevices
                            |> List.tryFind (fun d -> d.id = id)
                            |> Option.iter (SelectMidiDevice >> dispatch))
                match model.selectedMidiDevice with
                | Some device ->
                    Html.p [ prop.className "hint"; prop.text (sprintf "Listening to %s" device.name) ]
                | None -> Html.none
            ]

            card "Master" [
                labeledSlider "Master Volume" 0.0 1.0 0.01
                    model.synth.master.volume formatPercent
                    (SetMasterVolume >> dispatch)
                labeledSlider "Synth Layer" 0.0 1.0 0.01
                    model.synth.synthVolume formatPercent
                    (SetSynthVolume >> dispatch)
                labeledSlider "Sample Layer" 0.0 1.0 0.01
                    model.synth.sampleVolume formatPercent
                    (SetSampleVolume >> dispatch)
                labeledSlider "Polyphony" 1.0 32.0 1.0
                    (float model.synth.master.polyphonyLimit)
                    (fun v -> sprintf "%d voices" (int v))
                    (int >> SetPolyphonyLimit >> dispatch)
            ]

            card "Quick Load" [
                if model.presets.IsEmpty then
                    Html.p [ prop.className "hint"; prop.text "No presets saved yet. Visit the Presets tab to create one." ]
                else
                    Html.div [
                        prop.className "quick-load-list"
                        prop.children [
                            for preset in model.presets |> List.truncate 6 ->
                                ghostButton preset.name (fun () -> dispatch (LoadPreset preset.name))
                        ]
                    ]
                match model.currentPresetName with
                | Some name ->
                    Html.p [ prop.className "hint"; prop.text (sprintf "Current preset: %s" name) ]
                | None -> Html.none
            ]
        ]
    ]
