/**
 * midiInput.js — Web MIDI API abstraction for the Fable synthesizer.
 *
 * Owns the MIDIAccess object and the active input subscription. The F# side
 * receives plain { type, note, velocity } objects through a callback.
 */

let midiAccess = null;
let activeInput = null;

// MIDI status byte high nibble values.
const NOTE_ON = 0x90;
const NOTE_OFF = 0x80;

export class MidiInput {
  /**
   * Request access to MIDI devices.
   * @returns {Promise<boolean>} resolves true on success; rejects with a
   * user-friendly Error if MIDI is unsupported or permission was denied.
   */
  static requestMidiAccess() {
    if (!navigator.requestMIDIAccess) {
      return Promise.reject(
        new Error("Web MIDI is not supported in this browser. Try Chrome or Edge.")
      );
    }
    return navigator
      .requestMIDIAccess({ sysex: false })
      .then((access) => {
        midiAccess = access;
        return true;
      })
      .catch((err) => {
        console.error("[MidiInput] MIDI access failed:", err);
        throw new Error("MIDI access was denied. Allow MIDI permissions and reload.");
      });
  }

  /**
   * List currently connected MIDI input devices.
   * @returns {{id: string, name: string}[]}
   */
  static getMidiInputs() {
    if (!midiAccess) return [];
    const inputs = [];
    midiAccess.inputs.forEach((input) => {
      inputs.push({ id: input.id, name: input.name || "Unknown MIDI device" });
    });
    return inputs;
  }

  /**
   * Subscribe to a MIDI input by id. Replaces any previous subscription.
   * @param {string} inputId
   * @param {(msg: {type: string, note: number, velocity: number}) => void} onMessage
   * @returns {boolean} true if the device was found and subscribed.
   */
  static selectMidiInput(inputId, onMessage) {
    if (!midiAccess) return false;
    MidiInput.closeMidiInput();

    const input = midiAccess.inputs.get(inputId);
    if (!input) {
      console.warn(`[MidiInput] device ${inputId} not found`);
      return false;
    }

    input.onmidimessage = (event) => {
      const [status, note, velocity] = event.data;
      const command = status & 0xf0;
      if (command === NOTE_ON && velocity > 0) {
        // Velocity is normalized to 0..1 for the audio engine.
        onMessage({ type: "noteOn", note, velocity: velocity / 127 });
      } else if (command === NOTE_OFF || (command === NOTE_ON && velocity === 0)) {
        // Many keyboards send note-on with velocity 0 instead of note-off.
        onMessage({ type: "noteOff", note, velocity: 0 });
      }
      // Other message types (CC, pitch bend, ...) are ignored in the MVP.
    };
    activeInput = input;
    return true;
  }

  /** Unsubscribe from the active input, if any. */
  static closeMidiInput() {
    if (activeInput) {
      activeInput.onmidimessage = null;
      activeInput = null;
    }
  }
}
