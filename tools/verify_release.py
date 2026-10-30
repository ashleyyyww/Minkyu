import hashlib
import io
import json
import pathlib
import sys
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
apk = root / "artifacts/Minkyu.apk"
if len(sys.argv) > 1:
    apk = pathlib.Path(sys.argv[1]).resolve()
with zipfile.ZipFile(apk) as archive:
    names = archive.namelist()
    if any(n.endswith((".pdb", ".mdb", ".pem", ".keystore", ".jks")) for n in names):
        raise ValueError("Unexpected symbols or keys")
    with zipfile.ZipFile(
        io.BytesIO(archive.read("assets/loader-payload.zip"))
    ) as payload:
        manifest = json.loads(archive.read("assets/loader-payload.json"))
        if set(payload.namelist()) != {r["path"] for r in manifest["files"]}:
            raise ValueError("Payload inventory mismatch")
        for record in manifest["files"]:
            data = payload.read(record["path"])
            if (
                len(data) != record["size"]
                or hashlib.sha256(data).hexdigest() != record["sha256"]
            ):
                raise ValueError("Payload hash mismatch")
        if any(
            n.endswith(
                ("global-metadata.dat", "libil2cpp.so", ".mdm", ".obb", ".pdb", ".mdb")
            )
            or "/Il2CppAssemblies/" in n
            for n in payload.namelist()
        ):
            raise ValueError("Unexpected game input or symbols")
        projects = ["CustomAlbums.Android", "Minkyu.Mods.Android"]
        if {n for n in payload.namelist() if n.startswith("assets/copyToBase/Mods/")} != {
            "assets/copyToBase/Mods/" + project + ".dll" for project in projects
        }:
            raise ValueError("Unexpected mod in release payload")
        for project in projects:
            expected = (
                root / "src" / project / "bin/Release/net8.0" / (project + ".dll")
            )
            if (
                payload.read("assets/copyToBase/Mods/" + project + ".dll")
                != expected.read_bytes()
            ):
                raise ValueError("Mod differs from this build")
    if "lib/arm64-v8a/libaot-Minkyu.dll.so" not in names:
        raise ValueError("Manager AOT library missing")
print("Verified APK payload and source-built mods")
