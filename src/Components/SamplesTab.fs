module Synth.Components.SamplesTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// Card for one uploaded sample: mode, note mapping, volume and removal.
let private sampleCard (sample: SampleData) (dispatch: Msg -> unit) : ReactElement =
    let bufferMissing = isNull sample.audioBuffer

    card sample.name [
        if bufferMissing then
            Html.p [
                prop.className "hint hint-warning"
                prop.text "Audio not loaded (restored from a preset). Re-upload the file to hear it."
            ]

        selectBox
            "Mode"
            [ "pitched", "Pitched (plays on every key)"
              "mapped", "Mapped (plays on one key)" ]
            ((SampleMode.toLabel sample.mode).ToLowerInvariant())
            (fun v -> dispatch (SetSampleMode(sample.id, SampleMode.ofString v)))

        if sample.mode = Mapped then
            // The mapping stores the MIDI note number as the map key.
            let currentNote =
                sample.mappings
                |> Map.toList
                |> List.tryHead
                |> Option.map fst
                |> Option.defaultValue "60"

            selectBox
                "Mapped Note"
                [ for midi, name in MidiNotes.mappable -> string midi, name ]
                currentNote
                (fun v -> dispatch (SetSampleNote(sample.id, v)))

        labeledSlider "Volume" 0.0 1.0 0.01
            sample.volume formatPercent
            (fun v -> dispatch (SetSampleVolumeFor(sample.id, v)))

        Html.div [
            prop.className "button-row"
            prop.children [
                dangerButton "Remove" (fun () -> dispatch (RemoveSample sample.id))
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "tab-grid"
        prop.children [

            card "Upload Sample" [
                Html.p [
                    prop.className "hint"
                    prop.text "Upload WAV/MP3/OGG files. Pitched samples follow the keyboard; mapped samples trigger on a single key."
                ]
                Html.label [
                    prop.className "file-drop"
                    prop.children [
                        Html.input [
                            prop.type' "file"
                            prop.accept "audio/*"
                            prop.className "file-input"
                            prop.ariaLabel "Upload audio sample"
                            prop.onChange (fun (file: Browser.Types.File) ->
                                dispatch (SampleFileSelected file))
                        ]
                        Html.span [ prop.text "Choose an audio file…" ]
                    ]
                ]
                labeledSlider "Sample Layer Volume" 0.0 1.0 0.01
                    model.synth.sampleVolume formatPercent
                    (SetSampleVolume >> dispatch)
            ]

            if model.synth.samples.IsEmpty then
                card "Samples" [
                    Html.p [ prop.className "hint"; prop.text "No samples uploaded yet." ]
                ]
            else
                for sample in model.synth.samples do
                    sampleCard sample dispatch
        ]
    ]
