module Synth.Components.Common

open Feliz

// ---------------------------------------------------------------------------
// Shared UI building blocks, styled by Styles/components.css.
// Every control is a plain function returning a ReactElement.
// ---------------------------------------------------------------------------

/// A rounded panel with an optional title (CleanMyMac "card").
let card (title: string) (children: ReactElement list) : ReactElement =
    Html.section [
        prop.className "card"
        prop.children [
            if title <> "" then
                Html.h3 [ prop.className "card-title"; prop.text title ]
            yield! children
        ]
    ]

/// A labeled horizontal slider with a live value readout.
/// `format` renders the current value (e.g. "440 Hz", "35%").
let labeledSlider
    (label: string)
    (min': float)
    (max': float)
    (step: float)
    (value: float)
    (format: float -> string)
    (onChange: float -> unit)
    : ReactElement =
    Html.div [
        prop.className "control-row"
        prop.children [
            Html.div [
                prop.className "control-header"
                prop.children [
                    Html.label [ prop.className "control-label"; prop.text label ]
                    Html.span [ prop.className "control-value"; prop.text (format value) ]
                ]
            ]
            Html.input [
                prop.className "slider"
                prop.type' "range"
                prop.min min'
                prop.max max'
                prop.step step
                prop.value value
                prop.ariaLabel label
                prop.onChange (fun (v: float) -> onChange v)
            ]
        ]
    ]

/// An animated on/off toggle switch with a label.
let toggle (label: string) (isOn: bool) (onChange: bool -> unit) : ReactElement =
    Html.div [
        prop.className "control-row toggle-row"
        prop.children [
            Html.label [ prop.className "control-label"; prop.text label ]
            Html.button [
                prop.className (if isOn then "switch switch-on" else "switch")
                prop.role "switch"
                prop.ariaChecked isOn
                prop.ariaLabel label
                prop.onClick (fun _ -> onChange (not isOn))
                prop.children [ Html.span [ prop.className "switch-thumb" ] ]
            ]
        ]
    ]

/// A labeled dropdown. `options` pairs the option value with its label.
let selectBox
    (label: string)
    (options: (string * string) list)
    (value: string)
    (onChange: string -> unit)
    : ReactElement =
    Html.div [
        prop.className "control-row"
        prop.children [
            Html.label [ prop.className "control-label"; prop.text label ]
            Html.select [
                prop.className "select"
                prop.value value
                prop.ariaLabel label
                prop.onChange (fun (v: string) -> onChange v)
                prop.children [
                    for v, text in options ->
                        Html.option [ prop.value v; prop.text text ]
                ]
            ]
        ]
    ]

/// Primary (cyan) action button.
let primaryButton (label: string) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.className "btn btn-primary"
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

/// Secondary (purple) action button.
let secondaryButton (label: string) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.className "btn btn-secondary"
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

/// Danger (red) action button, for destructive operations.
let dangerButton (label: string) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.className "btn btn-danger"
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

/// Subtle "ghost" button for low-emphasis actions.
let ghostButton (label: string) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.className "btn btn-ghost"
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

/// A small status pill: green when ok, gray otherwise.
let statusBadge (label: string) (ok: bool) : ReactElement =
    Html.span [
        prop.className (if ok then "badge badge-ok" else "badge")
        prop.children [
            Html.span [ prop.className "badge-dot" ]
            Html.text label
        ]
    ]

// ---------------------------------------------------------------------------
// Value formatters used across tabs
// ---------------------------------------------------------------------------

let formatSeconds (v: float) : string = sprintf "%.2f s" v
let formatPercent (v: float) : string = sprintf "%.0f%%" (v * 100.0)
let formatSemitones (v: float) : string = sprintf "%+d st" (int v)
let formatCents (v: float) : string = sprintf "%+.0f ct" (v * 100.0)

let formatHz (v: float) : string =
    if v >= 1000.0 then sprintf "%.1f kHz" (v / 1000.0) else sprintf "%.0f Hz" v
