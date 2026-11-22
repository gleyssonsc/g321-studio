param(
    [Parameter(Mandatory)]
    [string]$ApoDirectory,
    [Parameter(Mandatory)]
    [string]$InnoCompiler,
    [string]$SignToolPath,
    [string]$CertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStoreLocation = 'CurrentUser',
    [uri]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$project = Join-Path $repo 'G321Studio.csproj'
$output = Join-Path $repo 'dist'
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).ProviderPath
$vendor = (Resolve-Path -LiteralPath $ApoDirectory).ProviderPath

$signing = $PSBoundParameters.ContainsKey('SignToolPath') -or
    $PSBoundParameters.ContainsKey('CertificateThumbprint') -or
    $PSBoundParameters.ContainsKey('CertificateStoreLocation') -or
    $PSBoundParameters.ContainsKey('TimestampUrl')
if ($signing) {
    if ([string]::IsNullOrWhiteSpace($SignToolPath) -or [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw 'Supply both -SignToolPath and -CertificateThumbprint to enable Authenticode signing.'
    }

    $thumbprint = ($CertificateThumbprint -replace '[\s\u200e\u200f\ufeff]', '').ToUpperInvariant()
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') {
        throw 'The signing certificate thumbprint must contain 40 hexadecimal digits.'
    }
    if (!$TimestampUrl.IsAbsoluteUri -or $TimestampUrl.Scheme -notin @('http', 'https') -or $TimestampUrl.UserInfo) {
        throw 'The RFC 3161 timestamp server must be an absolute HTTP or HTTPS URL without credentials.'
    }

    $signTool = (Resolve-Path -LiteralPath $SignToolPath).ProviderPath
    if (!(Test-Path -LiteralPath $signTool -PathType Leaf) -or [IO.Path]::GetFileName($signTool) -ne 'signtool.exe') {
        throw 'The SignTool path must point to signtool.exe from the Windows SDK.'
    }

    $certificate = Get-Item -LiteralPath "Cert:\$CertificateStoreLocation\My\$thumbprint" -ErrorAction SilentlyContinue
    $now = Get-Date
    if (!$certificate -or !$certificate.HasPrivateKey -or $certificate.NotBefore -gt $now -or
        $certificate.NotAfter -le $now -or $certificate.EnhancedKeyUsageList.ObjectId -notcontains '1.3.6.1.5.5.7.3.3') {
        throw "No active code signing certificate with an available private key matched in $CertificateStoreLocation\My."
    }

    $signArguments = @('sign', '/s', 'My')
    if ($CertificateStoreLocation -eq 'LocalMachine') {
        $signArguments += '/sm'
    }
    $signArguments += @('/sha1', $thumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256')
}

function Assert-AuthenticodeSignature([string]$File) {
    & $signTool verify /pa /all /tw $File
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool verification failed for '$File' (exit $LASTEXITCODE)."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $File
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint -or !$signature.TimeStamperCertificate) {
        throw "Expected a valid Authenticode signature and timestamp from the selected signer on '$File'."
    }
}

$hashes = @{
    'EqualizerAPO-x64-1.4.2.exe' = '7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa'
    'EqualizerAPO-x86-1.4.2.exe' = '15bdaba028b8a7805e17805e4596c27ab240381b5d5044cf247fa80717341b35'
    'EqualizerAPO-ARM64-1.4.2.exe' = '0dbfc64dee60f94cf77b297feb1f54d2633d49669bb474086fffb8db23e8f066'
    'EqualizerAPO-src-1.4.2.zip' = 'bacd5c78be71f3aaab010684ab39f0e5c1bbb45fcd75cbb92c103a145a731dc4'
}

$payload = [IO.Directory]::CreateTempSubdirectory('g321-setup-').FullName
$vendorPayload = Join-Path $payload 'vendor'
$notices = Join-Path $payload 'notices'
$compiledOutput = Join-Path $payload 'output'
$null = New-Item -ItemType Directory -Path $vendorPayload, $notices, $compiledOutput

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
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None',
    '-p:IncludeSourceRevisionInInformationalVersion=false', '--nologo'
)
foreach ($architecture in 'x64', 'x86', 'arm64') {
    $appOutput = Join-Path $payload "app\$architecture"
    dotnet publish $project @publish -r "win-$architecture" --artifacts-path (Join-Path $payload "build-$architecture") -o $appOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for win-$architecture (exit $LASTEXITCODE)."
    }
    if ($signing) {
        $app = Join-Path $appOutput 'G321 Studio.exe'
        & $signTool @signArguments $app
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed for win-$architecture (exit $LASTEXITCODE)."
        }
        Assert-AuthenticodeSignature $app
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
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $notices 'G321Studio-LICENSE.txt')

$compilerOptions = @(
    "/DPayloadRoot=$payload"
    "/DOutputFolder=$compiledOutput"
    "/DApoX64Sha256=$($hashes['EqualizerAPO-x64-1.4.2.exe'])"
    "/DApoX86Sha256=$($hashes['EqualizerAPO-x86-1.4.2.exe'])"
    "/DApoArm64Sha256=$($hashes['EqualizerAPO-ARM64-1.4.2.exe'])"
)
if ($signing) {
    $quotedCommand = (@($signTool) + $signArguments | ForEach-Object {
        '$q{0}$q' -f $_.Replace('$', '$$')
    }) -join ' '
    $compilerOptions += '/DAuthenticodeSign=1', ('/SG321Authenticode=' + $quotedCommand + ' $f')
}
& $compiler @compilerOptions (Join-Path $PSScriptRoot 'G321Studio.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed (exit $LASTEXITCODE)."
}

$setup = Join-Path $compiledOutput 'G321 Studio Setup.exe'
if ($signing) {
    Assert-AuthenticodeSignature $setup
}

$null = New-Item -ItemType Directory -Path $output -Force
$destination = Join-Path $output 'G321 Studio Setup.exe'
$incoming = Join-Path $output ('.g321-setup-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::Copy($setup, $incoming)
    [IO.File]::Move($incoming, $destination, $true)
} finally {
    [IO.File]::Delete($incoming)
}

Get-FileHash -LiteralPath $destination -Algorithm SHA256
