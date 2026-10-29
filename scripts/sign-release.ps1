param([string]$AndroidSdkDirectory = $env:ANDROID_HOME)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('minkyu-sign-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    if (-not $AndroidSdkDirectory) { throw 'Set ANDROID_HOME.' }
    foreach ($name in @('MINKYU_RELEASE_KEYSTORE_BASE64', 'MINKYU_RELEASE_PASSWORD', 'MINKYU_LEGACY_KEYSTORE_BASE64')) {
        if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Missing release signing secret: $name" }
    }
    $lineage = Join-Path $root 'build-support/minkyu.lineage'
    if (-not (Test-Path -LiteralPath $lineage -PathType Leaf)) { throw 'Release signing lineage is missing.' }

    $key = Join-Path $temporary 'release.keystore'
    $legacy = Join-Path $temporary 'legacy.keystore'
    $password = Join-Path $temporary 'release-password.txt'
    [IO.File]::WriteAllBytes($key, [Convert]::FromBase64String($env:MINKYU_RELEASE_KEYSTORE_BASE64))
    [IO.File]::WriteAllBytes($legacy, [Convert]::FromBase64String($env:MINKYU_LEGACY_KEYSTORE_BASE64))
    [IO.File]::WriteAllText($password, $env:MINKYU_RELEASE_PASSWORD)

    $signer = Join-Path $AndroidSdkDirectory 'build-tools/36.0.0/apksigner.bat'
    $inputApk = Join-Path $root 'artifacts/Minkyu-unsigned.apk'
    $output = Join-Path $root 'artifacts/Minkyu.apk'
    & $signer sign --lineage $lineage --rotation-min-sdk-version 28 --ks $legacy --ks-key-alias androiddebugkey --ks-pass pass:android --next-signer --ks $key --ks-key-alias minkyu --ks-pass "file:$password" --out $output $inputApk
    if ($LASTEXITCODE -ne 0) { throw 'APK signing failed' }
    $verification = & $signer verify --verbose --print-certs $output
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
    $verification | Write-Output
    # Refuse a different key: existing installs must remain updatable.
    $fingerprint = '006018fc090db4abface75f349449dd51833972fc5b9e6ed15f6a58d2953972d'
    if ($verification -notcontains "Signer #1 certificate SHA-256 digest: $fingerprint") {
        throw 'APK is not signed with the existing Minkyu release certificate.'
    }
}
finally {
    foreach ($name in @('release.keystore', 'release-password.txt', 'legacy.keystore')) {
        $path = Join-Path $temporary $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $temporary
}
