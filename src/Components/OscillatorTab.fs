module Synth.Components.OscillatorTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// One card per oscillator slot (enable, waveform, tuning, level).
let private oscillatorCard
    (index: int)
    (osc: OscillatorParams)
    (dispatch: Msg -> unit)
    : ReactElement =
    card (sprintf "Oscillator %d" (index + 1)) [
        toggle "Enabled" osc.enabled (fun on -> dispatch (SetOscillatorEnabled(index, on)))

        selectBox
            "Waveform"
            [ for w in WaveformType.all -> WaveformType.toJs w, WaveformType.toLabel w ]
            (WaveformType.toJs osc.waveformType)
            (fun v -> dispatch (SetOscillatorWaveform(index, WaveformType.ofString v)))

        labeledSlider "Coarse Tune" -24.0 24.0 1.0
            (float osc.coarseTune) formatSemitones
            (fun v -> dispatch (SetOscillatorCoarseTune(index, int v)))

        labeledSlider "Fine Tune" -0.5 0.5 0.01
            osc.fineTune formatCents
            (fun v -> dispatch (SetOscillatorFineTune(index, v)))

        labeledSlider "Volume" 0.0 1.0 0.01
            osc.volume formatPercent
            (fun v -> dispatch (SetOscillatorVolume(index, v)))
    ]

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "tab-grid"
        prop.children [
            for i, osc in List.indexed model.synth.oscillators ->
                oscillatorCard i osc dispatch
        ]
    ]
