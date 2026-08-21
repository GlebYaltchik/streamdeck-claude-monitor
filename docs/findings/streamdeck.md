# Finding: the device, the protocol, and what the SDK is worth

**Everything the plugin needs works, and no SDK library is required.**

Probed with `src/ClaudeDeck.Plugin` speaking the Elgato WebSocket protocol directly against
Stream Deck 7.5.0.22885 on Windows.

## What the device reports

```json
{ "name": "Stream Deck + XL", "type": 13, "size": { "columns": 9, "rows": 4 } }
```

36 keys, `devicePixelRatio: 2`, so a 144×144 key canvas is correct.

**The encoder count is not in the protocol.** `deviceDidConnect` and the launch `info`
payload describe only the key grid; nothing anywhere reports how many dials exist. The only
handle on it is `type: 13`.

That matters for the library question. A device type this new will be unknown to any SDK
wrapper until it is updated, and a wrapper that maps type numbers to device capabilities will
either mis-describe the device or reject it. Talking to the protocol directly sidesteps the
problem entirely, because the protocol does not care: actions are placed by the user, and the
plugin only ever responds to the events it is given.

## Keypad

`willAppear` arrives with `payload.controller: "Keypad"` and grid coordinates.

`setImage` with an SVG data URL **renders correctly on the hardware** — confirmed visually.
This closes the question opened in [rendering.md](rendering.md): the project needs no imaging
library.

`keyDown` and `keyUp` are delivered as separate events, which is what a long-press gate needs
(design §6.4). Press duration is measured by the plugin from the gap between them.

## Encoder

`willAppear` arrives with `payload.controller: "Encoder"` and its own coordinate space — the
first dial is `{column: 0, row: 0}`, independent of the key grid.

`dialRotate` carries `ticks` (signed) and a `pressed` flag, so rotation-while-pressed is
distinguishable from plain rotation without extra state.

`setFeedback` against the `$B1` layout updates the touch strip: `title`, `value` and
`indicator` all render, and the indicator bar tracks the value.

## Coalescing is not optional

A few seconds of turning one dial produced **116 `dialRotate` events**, and the probe answered
every one with a `setFeedback`. That is roughly 230 messages over the socket for one gesture.

Design §8 budgets updates at about 4 Hz with a dirty flag per key. This measurement turns that
from a precaution into a requirement: a single fast spin will otherwise saturate the
connection, and the same applies to any state that changes rapidly.

## `setImage` does not animate

Measured while looking for a way to make a slot ask for attention without redrawing it.

- **An animated SVG is rasterized once.** A background carrying both a SMIL `<animate>` and a
  CSS `@keyframes` — verified to run in a browser — sat still on the device.
- **An animated GIF is shown as a still.** A two-frame 144x144 GIF was hand-built, verified
  frame by frame with a decoder written for the purpose, delivered through `setImage`, and
  appeared on the key as one unchanging square.

Animated key images do work when they are set through the Stream Deck application's own
interface, where the application decodes and drives them. That is not the same path as a
plugin's `setImage`, and it is not available to us.

**So movement on a key costs one message per frame.** The 4 Hz figure in design §8 is a budget
taken from that document, not a measured ceiling: the 116-event measurement below is about
answering an input flood, not about how fast one key can be pushed. What a single animating
key can sustain is still unmeasured.

**There is a title layer.** `setTitle` draws text over the image, and the manifest sets its
font, size and colour per state. An earlier claim here that a key is one picture with no text
layer was wrong.

## Revision: drop the SDK library

Design §9 planned to start on BarRaider's StreamDeck-Tools with all SDK contact isolated
behind `IDeckConnection`, and to fall back to the raw protocol if the library did not
understand the device.

The probe reached a working plugin — registration, both controller types, SVG keys, encoder
feedback, key and dial input — in about 200 lines with no dependencies. The fallback is
cheaper than the thing it was a fallback for.

**Use the protocol directly.** Keep `IDeckConnection` anyway: it is what makes the renderer
testable without a device, and it costs nothing now that it wraps our own code.

## The probe plugin was removed

It was installed only to answer these questions and has been uninstalled. The sources stay in
`src/ClaudeDeck.Plugin`, which is where the real plugin grows from in Phase 1.
