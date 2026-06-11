module Synth.Components.EnvelopeTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// Amplitude (volume) ADSR envelope for the synth voices.
let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let env = model.synth.envelope

    Html.div [
        prop.className "tab-grid"
        prop.children [
            card "Amplitude Envelope" [
                Html.p [
                    prop.className "hint"
                    prop.text "Shapes the volume of every note: rise (attack), fall to the held level (decay/sustain), and fade after release."
                ]
                labeledSlider "Attack" 0.0 5.0 0.01
                    env.attack formatSeconds
                    (SetEnvelopeAttack >> dispatch)
                labeledSlider "Decay" 0.0 5.0 0.01
                    env.decay formatSeconds
                    (SetEnvelopeDecay >> dispatch)
                labeledSlider "Sustain" 0.0 1.0 0.01
                    env.sustain formatPercent
                    (SetEnvelopeSustain >> dispatch)
                labeledSlider "Release" 0.0 5.0 0.01
                    env.release formatSeconds
                    (SetEnvelopeRelease >> dispatch)
            ]
        ]
    ]
