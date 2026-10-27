# native/

LightGBM's native library goes here. It is not committed to git; fetch it with:

- Windows: `./tools/get-lightgbm.ps1` → `native/win-x64/lib_lightgbm.dll`
- Linux / macOS: `bash tools/get-lightgbm.sh` → `native/linux-x64/lib_lightgbm.so` or `native/osx/lib_lightgbm.dylib`

Both scripts download the official LightGBM 4.5.0 wheel from PyPI and verify its SHA-256 before extracting the library.
`src/Occlet.Approach2` copies the library next to its executable at build time.
