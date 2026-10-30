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
    $bootstrap = Join-Path $root 'build/bootstrap'
    New-Item -ItemType Directory -Force "$bootstrap/classes", "$bootstrap/dex" | Out-Null
    & "$JavaSdkDirectory/bin/javac.exe" --release 8 -classpath "$AndroidSdkDirectory/platforms/android-36/android.jar" -d "$bootstrap/classes" src/Minkyu.Bootstrap/GameActivity.java
    if ($LASTEXITCODE -ne 0) { throw 'Game bootstrap compilation failed' }
    & "$JavaSdkDirectory/bin/jar.exe" cf "$bootstrap/bootstrap.jar" -C "$bootstrap/classes" .
    if ($LASTEXITCODE -ne 0) { throw 'Game bootstrap archive failed' }
    & "$AndroidSdkDirectory/build-tools/36.0.0/d8.bat" --release --min-api 24 --lib "$AndroidSdkDirectory/platforms/android-36/android.jar" --output "$bootstrap/dex" "$bootstrap/bootstrap.jar"
    if ($LASTEXITCODE -ne 0) { throw 'Game bootstrap dex compilation failed' }
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
