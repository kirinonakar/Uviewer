# Pele 1.12.0

`pele.global.js` is the unmodified global bundle from the `pele@1.12.0` npm package:

- Package: https://registry.npmjs.org/pele/-/pele-1.12.0.tgz
- Package SHA-1: `fa061823c67e222f3671641ab3ef0442b9030068`
- Upstream: https://github.com/obsidianmd/pele
- License: MIT, reproduced in `LICENSE.txt` and copied to app output.

The bundle is embedded in the application and executed by Jint without browser,
Node.js, filesystem, or network APIs. Svg.Skia rasterizes its SVG for the native
inline image renderer. Diagram links and remote images are disabled.

Markdown fences labeled `mermaid` or `pele` use this renderer. A rendering failure
leaves the original code block visible with an error message. Ordinary code fences
continue to display as text.

Document parsing stores diagram source without loading the JavaScript/SVG runtime.
Only diagrams in the Markdown viewport are rendered, on a worker thread through
the image resource loader. Concurrent requests for one diagram share its pending
load. Successful renders reuse the initialized Jint engine and a bounded image
cache; failed renders reset the engine and replace the image block with the error
and complete source while preserving the block index and source anchor.

Diagrams rasterize at 4x SVG resolution. Win2D receives the corresponding DPI,
and the inline layout uses logical dimensions, preserving the existing display
size while providing 2x pixel density at 200% Windows scaling. Very large diagrams
are bounded to 8192 pixels per axis and 16 megapixels (64 MiB of RGBA).
