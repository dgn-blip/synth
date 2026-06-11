module Synth.Components.EffectsTab

open Feliz
open Synth.Types
open Synth.Components.Common

/// Effects chain: distortion → delay → reverb, with a master enable switch.
let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let fx = model.synth.effects

    Html.div [
        prop.className "tab-grid"
        prop.children [

            card "Effects" [
                toggle "Effects Enabled" fx.enabled (SetEffectsEnabled >> dispatch)
                Html.p [
                    prop.className "hint"
                    prop.text "Signal flows through distortion, then delay, then reverb."
                ]
            ]

            card "Distortion" [
                labeledSlider "Drive" 0.0 1.0 0.01
                    fx.distortion.drive formatPercent
                    (SetDistortionDrive >> dispatch)
                labeledSlider "Tone" 0.0 1.0 0.01
                    fx.distortion.tone formatPercent
                    (SetDistortionTone >> dispatch)
            ]

            card "Delay" [
                labeledSlider "Time" 0.1 2.0 0.01
                    fx.delay.delayTime formatSeconds
                    (SetDelayTime >> dispatch)
                labeledSlider "Feedback" 0.0 0.9 0.01
                    fx.delay.feedback formatPercent
                    (SetDelayFeedback >> dispatch)
                labeledSlider "Wet / Dry" 0.0 1.0 0.01
                    fx.delay.wetDry formatPercent
                    (SetDelayWetDry >> dispatch)
            ]

            card "Reverb" [
                labeledSlider "Room Size" 0.0 1.0 0.01
                    fx.reverb.roomSize formatPercent
                    (SetReverbRoomSize >> dispatch)
                labeledSlider "Wet / Dry" 0.0 1.0 0.01
                    fx.reverb.wetDry formatPercent
                    (SetReverbWetDry >> dispatch)
            ]
        ]
    ]
