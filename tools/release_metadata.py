import hashlib
import json
import os
import pathlib
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
apk = root / "artifacts/Minkyu.apk"
digest = hashlib.sha256(apk.read_bytes()).hexdigest()
commit = subprocess.check_output(
    ["git", "rev-parse", "HEAD"], cwd=root, text=True
).strip()
data = {
    "commit": commit,
    "tag": os.environ.get("GITHUB_REF_NAME", "local"),
    "workflow": os.environ.get("GITHUB_WORKFLOW_REF", "local"),
    "run": "https://github.com/"
    + os.environ.get("GITHUB_REPOSITORY", "ashleyyyww/Minkyu")
    + "/actions/runs/"
    + os.environ.get("GITHUB_RUN_ID", "local"),
    "apk": {"name": apk.name, "sha256": digest},
    "dependencyLockSha256": hashlib.sha256(
        (root / "dependencies.lock.json").read_bytes()
    ).hexdigest(),
    "referenceBundleSha256": hashlib.sha256(
        (root / "build-support/android-6.7.0-reference.zip").read_bytes()
    ).hexdigest(),
}
(root / "artifacts/build.json").write_text(json.dumps(data, indent=2))
(root / "artifacts/SHA256SUMS").write_text(digest + "  Minkyu.apk\n")
print(digest + "  Minkyu.apk")
