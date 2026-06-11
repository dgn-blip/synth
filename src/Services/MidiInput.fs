module Synth.Services.MidiInput

open Fable.Core
open Synth.Types

// ---------------------------------------------------------------------------
// Interop surface of js/midiInput.js
// ---------------------------------------------------------------------------

/// A MIDI message delivered by the JS layer.
type MidiMessage =
    abstract ``type``: string   // "noteOn" | "noteOff"
    abstract note: int          // 0-127
    abstract velocity: float    // already normalized to 0.0-1.0

/// A MIDI input device as reported by the JS layer.
type MidiDeviceJs =
    abstract id: string
    abstract name: string

/// Shape of the static `MidiInput` class exported by midiInput.js.
type IMidiInput =
    abstract requestMidiAccess: unit -> JS.Promise<bool>
    abstract getMidiInputs: unit -> ResizeArray<MidiDeviceJs>
    abstract selectMidiInput: inputId: string * onMessage: (MidiMessage -> unit) -> bool
    abstract closeMidiInput: unit -> unit

[<ImportMember("../../js/midiInput.js")>]
let MidiInput: IMidiInput = jsNative

// ---------------------------------------------------------------------------
// Typed helpers
// ---------------------------------------------------------------------------

/// Ask the browser for MIDI access. Rejects with a user-friendly error.
let requestAccess () : JS.Promise<bool> =
    MidiInput.requestMidiAccess ()

/// List connected MIDI input devices as domain records.
let listDevices () : MidiDevice list =
    MidiInput.getMidiInputs ()
    |> Seq.map (fun d -> { id = d.id; name = d.name })
    |> List.ofSeq

/// Subscribe to a device. The callback dispatches Elmish messages, so MIDI
/// events flow through the normal update loop.
let subscribe (deviceId: string) (dispatch: Msg -> unit) : bool =
    MidiInput.selectMidiInput (
        deviceId,
        fun msg ->
            match msg.``type`` with
            | "noteOn" -> dispatch (MidiNoteOn(msg.note, msg.velocity))
            | "noteOff" -> dispatch (MidiNoteOff msg.note)
            | _ -> ()
    )

let unsubscribe () : unit = MidiInput.closeMidiInput ()
