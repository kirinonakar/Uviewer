# Bundled JPEG XL decoder

`UviewerJxl.dll` is a Windows x64 decoder built from
[`UviewerJxl.cpp`](UviewerJxl.cpp) and the official
[libjxl v0.13.0 x64 static release](https://github.com/libjxl/libjxl/releases/tag/v0.13.0).
It links libjxl, its color management and threading libraries, Highway,
and Brotli. The DLL imports only `KERNEL32.dll`. The app copies the DLL
and its license and patent notices into `Libs/libjxl` in build and publish output.

- Release archive `jxl-x64-windows-static.7z` SHA-256
  `D97B753FFCD0F992DB2AB3EF7E896A6BA425CAFE8332E5E6DF6376F70E2B9ADA`
- `UviewerJxl.dll` SHA-256
  `32A7970D396C0E2CB094A9962F034C3543A759E771CE2164D6D3FD836D927742`

Run the following from an x64 Visual Studio Developer Command Prompt with the
current directory set to this directory. Set `JXL_STATIC_RELEASE` to the
extracted `x64-windows-static` directory. The checked-in binary was built
with MSVC 19.51.

```bat
cl /nologo /std:c++17 /O2 /MT /EHsc /LD /W4 /Brepro ^
  /DJXL_STATIC_DEFINE /DJXL_CMS_STATIC_DEFINE /DJXL_THREADS_STATIC_DEFINE ^
  /I"%JXL_STATIC_RELEASE%\include" UviewerJxl.cpp ^
  /link /Brepro /LIBPATH:"%JXL_STATIC_RELEASE%\lib" jxl.lib jxl_cms.lib ^
  jxl_threads.lib hwy.lib brotlidec.lib brotlienc.lib brotlicommon.lib ^
  /OUT:UviewerJxl.dll
```
