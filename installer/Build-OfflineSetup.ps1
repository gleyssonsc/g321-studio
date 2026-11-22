param(
    [Parameter(Mandatory)]
    [string]$ApoDirectory,
    [Parameter(Mandatory)]
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$project = Join-Path $repo 'G321Studio.csproj'
$output = Join-Path $repo 'dist'
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).ProviderPath
$vendor = (Resolve-Path -LiteralPath $ApoDirectory).ProviderPath

$hashes = @{
    'EqualizerAPO-x64-1.4.2.exe' = '7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa'
    'EqualizerAPO-x86-1.4.2.exe' = '15bdaba028b8a7805e17805e4596c27ab240381b5d5044cf247fa80717341b35'
    'EqualizerAPO-ARM64-1.4.2.exe' = '0dbfc64dee60f94cf77b297feb1f54d2633d49669bb474086fffb8db23e8f066'
    'EqualizerAPO-src-1.4.2.zip' = 'bacd5c78be71f3aaab010684ab39f0e5c1bbb45fcd75cbb92c103a145a731dc4'
}

$payload = [IO.Directory]::CreateTempSubdirectory('g321-setup-').FullName
$vendorPayload = Join-Path $payload 'vendor'
$notices = Join-Path $payload 'notices'
$null = New-Item -ItemType Directory -Path $vendorPayload, $notices

foreach ($name in $hashes.Keys) {
    $destination = Join-Path $vendorPayload $name
    Copy-Item -LiteralPath (Join-Path $vendor $name) -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hashes[$name]) {
        throw "Equalizer APO payload failed SHA-256 verification: $name"
    }
}

$publish = @(
    '-c', 'Release', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '--nologo'
)
foreach ($architecture in 'x64', 'x86', 'arm64') {
    dotnet publish $project @publish -r "win-$architecture" --artifacts-path (Join-Path $payload "build-$architecture") -o (Join-Path $payload "app\$architecture")
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for win-$architecture (exit $LASTEXITCODE)."
    }
}

$restore = Get-Content -Raw -LiteralPath (Join-Path $payload 'build-x64\obj\G321Studio\project.assets.json') | ConvertFrom-Json -AsHashtable
$runtimeDependencies = $restore.project.frameworks.Values.downloadDependencies
$runtimeNotices = @(
    @{ Package = 'Microsoft.NETCore.App.Runtime.win-x64'; File = 'LICENSE.TXT'; Name = 'dotnet-LICENSE.txt' },
    @{ Package = 'Microsoft.NETCore.App.Runtime.win-x64'; File = 'THIRD-PARTY-NOTICES.TXT'; Name = 'dotnet-THIRD-PARTY-NOTICES.txt' },
    @{ Package = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; File = 'LICENSE'; Name = 'WindowsDesktop-LICENSE.txt' }
)
foreach ($notice in $runtimeNotices) {
    $runtime = $runtimeDependencies | Where-Object name -eq $notice.Package | Select-Object -First 1
    $version = ($runtime.version -split ',')[0].TrimStart('[')
    $relative = Join-Path $runtime.name.ToLowerInvariant() (Join-Path $version $notice.File)
    $source = $restore.packageFolders.Keys | ForEach-Object { Join-Path $_ $relative } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$source) {
        throw "Could not find the notice for $($notice.Package) in the restored packages."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $notices $notice.Name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NAudio-LICENSE.txt') -Destination $notices

$null = New-Item -ItemType Directory -Path $output -Force
$compilerOptions = @(
    "/DPayloadRoot=$payload"
    "/DOutputFolder=$output"
    "/DApoX64Sha256=$($hashes['EqualizerAPO-x64-1.4.2.exe'])"
    "/DApoX86Sha256=$($hashes['EqualizerAPO-x86-1.4.2.exe'])"
    "/DApoArm64Sha256=$($hashes['EqualizerAPO-ARM64-1.4.2.exe'])"
)
& $compiler @compilerOptions (Join-Path $PSScriptRoot 'G321Studio.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed (exit $LASTEXITCODE)."
}

Get-FileHash -LiteralPath (Join-Path $output 'G321 Studio Setup.exe') -Algorithm SHA256
