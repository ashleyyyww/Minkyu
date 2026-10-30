import datetime
import hashlib
import json
import os
import pathlib
import subprocess
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
out = root / "dependencies/loader"
out.mkdir(parents=True, exist_ok=True)
work = root / "build/payload"
work.mkdir(parents=True, exist_ok=True)
patcher = root / "src/LoaderPatcher/bin/Release/net10.0/LoaderPatcher.dll"
loader = root / "tools/melon_data"


def patch(flag, source, *args):
    output = work / (flag.removeprefix("--") + ".dll")
    subprocess.run(
        ["dotnet", str(patcher), flag, str(source), *map(str, args), str(output)],
        check=True,
    )
    return output


runtime = patch(
    "--bridge-init-setters", loader / "MelonLoader/net8/Il2CppInterop.Runtime.dll"
)
runtime = patch(
    "--fix-arm64-generic",
    runtime,
    root / "src/LoaderFixes/bin/Release/net8.0/LoaderFixes.dll",
)
runtime = patch("--fix-arm64-field-default", runtime)
support = patch(
    "--embed-scripting-guard",
    loader / "MelonLoader/Dependencies/SupportModules/Il2Cpp.dll",
    root / "src/LoaderFixes/bin/Release/net8.0/LoaderFixes.dll",
)
managed = patch("--offline-assembly", loader / "MelonLoader/net8/MelonLoader.dll")
managed = patch(
    "--fix-android-first-run",
    managed,
    root / "src/LoaderFixes.Android/bin/Release/net8.0/LoaderFixes.Android.dll",
)
files = {}
files["assets/minkyu-bootstrap.dex"] = (root / "build/bootstrap/dex/classes.dex").read_bytes()
with zipfile.ZipFile(root / "dependencies/melon_data.zip") as source:
    for entry in source.infolist():
        if entry.is_dir() or entry.filename.lower().endswith((".pdb", ".mdb")):
            continue
        if entry.filename.startswith("native/"):
            name = "lib/arm64-v8a/" + pathlib.PurePosixPath(entry.filename).name
        else:
            name = "assets/" + entry.filename
        files[name] = source.read(entry)
for name in list(files):
    if name.endswith("/Il2CppInterop.Runtime.dll"):
        files[name] = runtime.read_bytes()
files["assets/MelonLoader/net8/MelonLoader.dll"] = managed.read_bytes()
files["assets/MelonLoader/Dependencies/SupportModules/Il2Cpp.dll"] = (
    support.read_bytes()
)
files["lib/arm64-v8a/libunity.so"] = (root / "dependencies/libunity.so").read_bytes()
files[
    "assets/copyToBase/MelonLoader/Dependencies/Il2CppAssemblyGenerator/UnityDependencies_2019.4.41.zip"
] = (root / "dependencies/UnityDependencies_2019.4.41.zip").read_bytes()
for name in ["CustomAlbums.Android", "Minkyu.Mods.Android"]:
    files["assets/copyToBase/Mods/" + name + ".dll"] = (
        root / "src" / name / "bin/Release/net8.0" / (name + ".dll")
    ).read_bytes()
for name in [
    "CustomAlbums.Core.dll",
    "Newtonsoft.Json.dll",
    "NVorbis.dll",
    "NLayer.dll",
]:
    files["assets/copyToBase/UserLibs/" + name] = (
        root / "src/CustomAlbums.Android/bin/Release/net8.0" / name
    ).read_bytes()
epoch = int(
    os.environ.get("SOURCE_DATE_EPOCH")
    or subprocess.check_output(
        ["git", "show", "-s", "--format=%ct", "HEAD"], cwd=root, text=True
    ).strip()
)
files["assets/lemon_patch_date.txt"] = (
    datetime.datetime.fromtimestamp(epoch, datetime.timezone.utc)
    .isoformat()
    .replace("+00:00", "Z")
    .encode()
)
records = []
with zipfile.ZipFile(
    out / "loader-payload.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=6
) as archive:
    for name, data in sorted(files.items()):
        entry = zipfile.ZipInfo(name, date_time=(2000, 1, 1, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(entry, data)
        records.append(
            {
                "path": name,
                "sha256": hashlib.sha256(data).hexdigest(),
                "size": len(data),
            }
        )
manifest = {
    "source": {
        "package": "com.prpr.musedash",
        "versionCode": 626,
        "libil2cppSha256": "e9713ee0a96778e0e24243b7bfac67302bf351ae6e246f5d821e58c41759e2f3",
        "globalMetadataSha256": "fa49bf33f529c28d159e16c29bbf2daeef3585dc533e0b4628dd9b9f62a251fd",
    },
    "files": records,
}
(out / "loader-payload.json").write_text(json.dumps(manifest, indent=2))
print("Built loader payload from source: " + str(len(records)) + " files")
