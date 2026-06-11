# Fable Synth

A browser-based synthesizer built with **F# (Fable)**, **Feliz/React**, **Elmish**, the **Web Audio API** and the **Web MIDI API**, styled with a CleanMyMac-inspired dark theme.

## Features

- 3 oscillators (sine / square / sawtooth / triangle) with coarse & fine tuning
- Polyphonic playback (configurable limit, oldest-note stealing)
- Amplitude ADSR envelope + filter ADSR envelope with modulation amount
- Lowpass / highpass / bandpass filter with cutoff & resonance
- Effects chain: distortion → delay → reverb, each with full controls
- Sample upload with pitched (whole keyboard) or mapped (single key) playback
- USB MIDI input with device selection
- Presets: save/load (localStorage), rename, delete, export/import as JSON

## Prerequisites

- [.NET SDK 8+](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- A Chromium-based browser for Web MIDI (Chrome / Edge)

## Run

```bash
npm install          # installs npm deps and restores the Fable dotnet tool
npm run dev          # compiles F# in watch mode and starts Vite
```

Open http://localhost:5173, plug in a MIDI keyboard and play.

## Build for production

```bash
npm run build        # outputs static files to dist/
```

## Project layout

```
src/
  Types.fs              Domain types, messages, defaults
  Services/             AudioEngine/MidiInput interop + preset persistence
  Components/           Shared controls + the 7 tab views
  Styles/               CleanMyMac-themed CSS (variables, base, components)
  App.fs                Elmish init/update/view + React 18 entry point
js/
  audioEngine.js        Web Audio graph, voices, samples, effects
  midiInput.js          Web MIDI access and note event normalization
```

Note: preset files store every parameter but **not** sample audio — re-upload
samples after reloading the page (the preset remembers their names, modes,
mappings and volumes).
