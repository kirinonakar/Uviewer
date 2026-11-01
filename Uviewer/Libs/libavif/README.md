# Bundled libavif

`../libavif.dll` is a Windows x64 Release build of **libavif 1.4.2**,
the latest stable upstream release checked on 2026-11-01. Uviewer calls its
C API directly; no managed codec package is needed.

- Source: https://github.com/AOMediaCodec/libavif/releases/tag/v1.4.2
- AV1 decoder: statically linked **libaom 3.14.1**, commit
  `03087864cf4bea6abb0d28f95cf7843511413d8f`.
- The AV1 encoder and optional external conversion libraries are disabled.
  CPU SIMD and multithreaded decoding are enabled. The MSVC runtime is linked
  statically; the DLL imports only `KERNEL32.dll`.
- Binary SHA-256:
  `9463D54AF862B402FDAFF9A8F6EC01795809321B080321576FADF0618E132ED6`.

The project copies `Libs/libavif.dll` and this directory's license notices to
both build and publish output. The checked-in DLL targets the project's x64
platform, like `Libs/7z.dll`. Other architectures require rebuilding the native
libraries for that architecture. The managed bindings check the native version
before reading C structures; update and validate them when upgrading the DLL.

## Rebuilding

Download and extract the official libavif v1.4.2 source archive. From an x64
Visual Studio developer shell with CMake, Ninja, Git, Perl and NASM in `PATH`
(this binary used Visual Studio 2026/MSVC 19.51 and NASM 2.16.03), run:

```powershell
cmake -S libavif-1.4.2 -B native-build -G Ninja `
  -DCMAKE_BUILD_TYPE=Release `
  -DBUILD_SHARED_LIBS=ON `
  -DAVIF_CODEC_AOM=LOCAL `
  -DAVIF_CODEC_AOM_ENCODE=OFF `
  -DAVIF_LIBYUV=OFF `
  -DAVIF_BUILD_APPS=OFF `
  -DAVIF_BUILD_TESTS=OFF `
  -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
  -DCONFIG_LIBYUV=0
cmake --build native-build --config Release --parallel
```

Upstream's `LocalAom.cmake` fetches libaom v3.14.1. Copy the resulting
`native-build/avif.dll` to `Uviewer/Libs/libavif.dll`. Keep libavif's `LICENSE`,
libaom's `LICENSE` and `PATENTS`, and the libaom `third_party/x86inc/LICENSE`
and `third_party/highway/LICENSE-BSD3` notices alongside this file.

## Verification

The DLL was loaded and decoded through `LibAvifDecoder` in a standalone .NET
harness using upstream v1.4.2 fixtures: SDR, PQ Display-P3/BT.2020, 8-bit and
12-bit animations, and animation with alpha. Checks covered BGRA8/RGBA16
buffers, color metadata, frame counts/timing, input lifetime across GC,
independent frame copies, asynchronous input, malformed/truncated input,
cancellation, repeated disposal, and loading from an unrelated working directory.
