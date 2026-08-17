# Finding: keys are drawn with SVG, not a raster canvas

Design §8 and §9 planned to render key images with SkiaSharp. That is unnecessary.

`setImage` accepts an SVG data URL:

```
data:image/svg+xml;base64,<base64 of the SVG document>
```

Confirmed by reading [Sing3Rous/stream-deck-ai-limits](https://github.com/Sing3Rous/stream-deck-ai-limits),
which renders all of its usage keys this way at a 144x144 canvas.

## Why this matters for us

Everything the session and usage keys need — a progress ring, a bar, a percentage, two lines
of text, a state colour — is a handful of SVG elements. Building that means composing a
string.

- **No imaging library.** SkiaSharp comes with native binaries per architecture, which would
  have complicated both the plugin build and any future non-Windows agent.
- **Resolution independence.** One SVG serves every Stream Deck model instead of rendering per
  device size.
- **Testable rendering.** Golden tests compare strings, not bitmaps. Bitmap comparison is
  fragile across platforms and font stacks; string comparison is exact.
- **Cheaper updates.** Composing a string per key is far below the cost of rasterizing, which
  matters given the coalescing budget in design §8.

## Consequences

- Drop SkiaSharp from the stack.
- The renderer becomes a small SVG-composition module with no dependencies.
- Fonts: text inside SVG is rendered by the Stream Deck client, so font availability is the
  client's concern. Stick to generic families and verify on the device in Step 4.
