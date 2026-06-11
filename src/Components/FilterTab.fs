module Synth.Components.FilterTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// Filter section: type, cutoff, resonance, plus its modulation envelope.
let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let filter = model.synth.filter

    Html.div [
        prop.className "tab-grid"
        prop.children [

            card "Filter" [
                selectBox
                    "Type"
                    [ for f in FilterType.all -> FilterType.toJs f, FilterType.toLabel f ]
                    (FilterType.toJs filter.filterType)
                    (fun v -> dispatch (SetFilterType(FilterType.ofString v)))

                // Cutoff covers 20 Hz - 20 kHz; a 1 Hz step keeps the slider smooth.
                labeledSlider "Cutoff" 20.0 20000.0 1.0
                    filter.cutoff formatHz
                    (SetFilterCutoff >> dispatch)

                labeledSlider "Resonance (Q)" 0.0 20.0 0.1
                    filter.resonance (sprintf "%.1f")
                    (SetFilterResonance >> dispatch)
            ]

            card "Filter Envelope" [
                Html.p [
                    prop.className "hint"
                    prop.text "Sweeps the cutoff upward on each note. Amount sets the depth."
                ]
                labeledSlider "Amount" 0.0 1.0 0.01
                    filter.envelopeAmount formatPercent
                    (SetFilterEnvelopeAmount >> dispatch)
                labeledSlider "Attack" 0.0 5.0 0.01
                    filter.envelope.attack formatSeconds
                    (SetFilterEnvelopeAttack >> dispatch)
                labeledSlider "Decay" 0.0 5.0 0.01
                    filter.envelope.decay formatSeconds
                    (SetFilterEnvelopeDecay >> dispatch)
                labeledSlider "Sustain" 0.0 1.0 0.01
                    filter.envelope.sustain formatPercent
                    (SetFilterEnvelopeSustain >> dispatch)
                labeledSlider "Release" 0.0 5.0 0.01
                    filter.envelope.release formatSeconds
                    (SetFilterEnvelopeRelease >> dispatch)
            ]
        ]
    ]
