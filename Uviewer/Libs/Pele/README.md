# Pele 1.12.0

`pele.global.js` is the unmodified global bundle from the `pele@1.12.0` npm package:

- Package: https://registry.npmjs.org/pele/-/pele-1.12.0.tgz
- Package SHA-1: `fa061823c67e222f3671641ab3ef0442b9030068`
- Upstream: https://github.com/obsidianmd/pele
- License: MIT, reproduced in `LICENSE.txt` and copied to app output.

The bundle is embedded in the application and executed by Jint without browser,
Node.js, filesystem, or network APIs. Svg.Skia rasterizes its SVG for the existing
native image-page renderer. Diagram links and remote images are disabled.

Markdown fences labeled `mermaid` or `pele` use this renderer. A rendering failure
leaves the original code block visible with an error message. Ordinary code fences
continue to display as text.

Run `dotnet run --project Checks/PeleRendering -c Release` from the repository root
for rendering smoke checks, optionally passing a directory after `--` to save PNGs.
