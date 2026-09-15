# Steamworks.NET dependency

`Steamworks.NET.dll` is built from the unmodified runtime sources of the official
[2025.163.0 release](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.163.0)
under the included MIT license. We target .NET 9 / AnyCPU, with upstream's
`STEAMWORKS_LIN_OSX` and `STEAMWORKS_X64` ABI constants (the latter is also used by
upstream's ARM64 configuration). The downloadable OSX/Linux standalone DLL targets
x64 specifically and cannot be loaded by this Mac's ARM64 .NET process.

Vendoring avoids network restores during ordinary packaging and pins the wrapper:

- Source: `https://github.com/rlabrecque/Steamworks.NET/archive/refs/tags/2025.163.0.tar.gz`
- Source archive SHA-256: `1ded6cbc297e9010266cae19ae07f75b805709cc0a6b7e3014160bb3fd21a657`
- Built assembly SHA-256: `708292a3ff6a562e670443f7070a7965e3ddcf6e66a92913fe215100fcfaa764`
- Build SDK used: .NET 9.0.317.

To rebuild after extracting the verified source archive:

```sh
dotnet build tools/WorkshopLocalization/vendor/Steamworks.NET.Build.csproj \
  -c Release -p:SteamworksSource=/absolute/path/to/extracted/source \
  --output /tmp/sts2-mod-steamworks-managed
cp /tmp/sts2-mod-steamworks-managed/Steamworks.NET.dll tools/WorkshopLocalization/vendor/
```

Packaging copies `libsteam_api.dylib` from the installed Mega Crit macOS uploader
into the prepared publisher directory. The native library must support the host
architecture. The current local library exports `SteamAPI_SteamUGC_v021`,
`SteamAPI_SteamUser_v023`, and `SteamAPI_SteamUtils_v010`. A .NET DllImport resolver
loads that exact prepared native library; initialization checks interface versions.

The publisher follows Valve's [ISteamUGC API](https://partner.steamgames.com/doc/api/ISteamUGC):
each listing update sets its language, title, and description explicitly; content
and its deduplication metadata are committed together. Native Steam is initialized
only by `publish` without `--dry-run`. Regression tests use an in-memory adapter.
