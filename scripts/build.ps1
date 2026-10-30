param(
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
try {
    if (-not $JavaSdkDirectory -or -not $AndroidSdkDirectory) {
        throw 'Set JAVA_HOME and ANDROID_HOME.'
    }
    foreach ($name in @('CustomAlbums.Android', 'Minkyu.Mods.Android', 'LoaderFixes', 'LoaderFixes.Android', 'LoaderPatcher')) {
        & dotnet build "src/$name" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
    }
    & python tools/build_payload.py
    if ($LASTEXITCODE -ne 0) { throw 'Payload build failed' }
    & dotnet build src/Minkyu.Android -c Release "-p:JavaSdkDirectory=$JavaSdkDirectory" "-p:AndroidSdkDirectory=$AndroidSdkDirectory" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Manager build failed' }
    New-Item -ItemType Directory -Force -Path artifacts | Out-Null
    Copy-Item -LiteralPath src/Minkyu.Android/bin/Release/net10.0-android/android-arm64/app.minkyu.manager.apk -Destination artifacts/Minkyu-unsigned.apk
}
finally {
    Pop-Location
}
