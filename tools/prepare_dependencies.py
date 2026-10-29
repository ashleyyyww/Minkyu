import hashlib
import json
import pathlib
import time
import urllib.request
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
lock = json.loads((root / "dependencies.lock.json").read_text())
for item in lock["downloads"]:
    target = root / item["path"]
    target.parent.mkdir(parents=True, exist_ok=True)
    if (
        not target.exists()
        or hashlib.sha256(target.read_bytes()).hexdigest() != item["sha256"]
    ):
        for attempt in range(3):
            try:
                request = urllib.request.Request(
                    item["url"], headers={"User-Agent": "Minkyu-build"}
                )
                with urllib.request.urlopen(request, timeout=120) as response:
                    data = response.read()
                if hashlib.sha256(data).hexdigest() != item["sha256"]:
                    raise ValueError("Download hash mismatch: " + item["path"])
                target.write_bytes(data)
                break
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(2 * (attempt + 1))
    print("Verified " + item["path"])
with zipfile.ZipFile(root / "dependencies/melon_data.zip") as archive:
    archive.extractall(root / "tools/melon_data")
reference = root / "build-support/android-6.7.0-reference.zip"
if hashlib.sha256(reference.read_bytes()).hexdigest() != lock["referenceBundleSha256"]:
    raise ValueError("Reference bundle hash mismatch")
with zipfile.ZipFile(reference) as archive:
    archive.extractall(root / "artifacts/interop-device/Il2CppAssemblies")
