# Minkyu

Play custom Muse Dash songs on Android

## Usage

1. Download `Minkyu.apk` from [Releases](https://github.com/ashleyyyww/Minkyu/releases).
2. Open Minkyu and set it up using your Muse Dash APK and OBB.
3. Install the modified game when prompted.
4. Import your `.mdm` songs, tap Play, and select Custom Albums in-game.

Make sure Muse Dash is closed when adding or removing songs.

## Requirements

- Android 7.0+ (ARM64)
- Muse Dash 6.7.0 (build 626)
- A standalone Muse Dash APK and matching OBB

Split APKs and games with an existing mod loader aren't supported.

## Notes

- Back up your game saves before setup. You may need to uninstall the original game to install the modified APK.
- Songs are stored in Minkyu after importing. Deleting a song removes its `.mdm` file.
- Uninstalling Minkyu also removes your imported songs and game-signing key, so back up your songs and avoid uninstalling when updating.
- MuseDashUnlockDLC is a built-in unlock patch for Muse Dash songs, characters, and other content. It only works with files already in the game, and some online scores may not upload.
- Supports up to 256 songs. Custom videos and dialogue aren't supported.

## Building

See [build instructions](docs/build.md).

## Credits

- [MDMC / CustomAlbums](https://github.com/MDMods/CustomAlbums) — chart conversion
- [LemonLoader](https://github.com/LemonLoader/MelonLoader), [Il2CppInterop](https://github.com/LemonLoader/Il2CppInterop) and [LemonLoader Installer](https://github.com/LemonLoader/MelonLoaderInstaller) — loader and APK tools

Licensed under [GPL-3.0](LICENSE). Other credits and licenses are in [NOTICE](NOTICE).
