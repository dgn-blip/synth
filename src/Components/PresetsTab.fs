module Synth.Components.PresetsTab

open Browser
open Feliz
open Synth.Types
open Synth.Components.Common

/// One row in the preset list: load / rename / delete.
let private presetRow (preset: Preset) (isCurrent: bool) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className (if isCurrent then "preset-row preset-row-current" else "preset-row")
        prop.children [
            Html.div [
                prop.className "preset-info"
                prop.children [
                    Html.span [ prop.className "preset-name"; prop.text preset.name ]
                    Html.span [
                        prop.className "preset-date"
                        prop.text (preset.timestamp.ToString("yyyy-MM-dd HH:mm"))
                    ]
                ]
            ]
            Html.div [
                prop.className "button-row"
                prop.children [
                    primaryButton "Load" (fun () -> dispatch (LoadPreset preset.name))
                    ghostButton "Rename" (fun () ->
                        // window.prompt keeps the MVP free of modal plumbing.
                        match Dom.window.prompt ("New preset name:", preset.name) with
                        | null -> ()
                        | newName when newName.Trim() <> "" ->
                            dispatch (RenamePreset(preset.name, newName.Trim()))
                        | _ -> ())
                    dangerButton "Delete" (fun () -> dispatch (DeletePreset preset.name))
                ]
            ]
        ]
    ]

/// Presets tab. A React component so the save-name input can keep local state.
[<ReactComponent>]
let View (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let presetName, setPresetName = React.useState ""

    Html.div [
        prop.className "tab-grid"
        prop.children [

            card "Save Preset" [
                Html.div [
                    prop.className "control-row"
                    prop.children [
                        Html.label [ prop.className "control-label"; prop.text "Preset name" ]
                        Html.input [
                            prop.className "text-input"
                            prop.type' "text"
                            prop.placeholder "e.g. Warm Pad"
                            prop.value presetName
                            prop.ariaLabel "Preset name"
                            prop.onChange (fun (v: string) -> setPresetName v)
                        ]
                    ]
                ]
                Html.div [
                    prop.className "button-row"
                    prop.children [
                        primaryButton "Save Preset" (fun () ->
                            let trimmed = presetName.Trim()
                            if trimmed <> "" then
                                dispatch (SavePreset trimmed)
                                setPresetName ""
                            else
                                dispatch (SetError "Enter a name before saving the preset."))
                    ]
                ]
                Html.p [
                    prop.className "hint"
                    prop.text "Presets store every parameter. Sample audio is not stored — re-upload samples after reloading the page."
                ]
            ]

            card "Import / Export" [
                Html.div [
                    prop.className "button-row"
                    prop.children [
                        secondaryButton "Export All as JSON" (fun () -> dispatch ExportPresets)
                    ]
                ]
                Html.label [
                    prop.className "file-drop"
                    prop.children [
                        Html.input [
                            prop.type' "file"
                            prop.accept ".json,application/json"
                            prop.className "file-input"
                            prop.ariaLabel "Import presets from JSON file"
                            prop.onChange (fun (file: Browser.Types.File) ->
                                dispatch (ImportPresetsFile file))
                        ]
                        Html.span [ prop.text "Import presets from JSON…" ]
                    ]
                ]
            ]

            card (sprintf "Saved Presets (%d)" model.presets.Length) [
                if model.presets.IsEmpty then
                    Html.p [ prop.className "hint"; prop.text "Nothing saved yet." ]
                else
                    Html.div [
                        prop.className "preset-list"
                        prop.children [
                            for preset in model.presets ->
                                presetRow
                                    preset
                                    (model.currentPresetName = Some preset.name)
                                    dispatch
                        ]
                    ]
            ]
        ]
    ]
