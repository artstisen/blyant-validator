#Requires -Version 7.3
# (System.Formats.Tar, which packs the versions for Linux and macOS, needs PowerShell 7.3 or later)

# Builds the release versions of Blyant Validator in the directory "publish", as Blyant's publish.ps1 does:
#   portable    The lightweight version for all platforms. It needs the .NET 10 runtime to be installed,
#               and is started with:  dotnet blyant-validator.dll
#   win-x64 ... A self-contained single file for each platform. The .NET runtime is included in the file,
#               so nothing has to be installed to run it.
# Each version is also packed for a release in "publish/release", named by the version: a zip for Windows and the
# portable version (BlyantValidator-0.4.0-win-x64.zip), and a tar.gz for Linux and macOS
# (BlyantValidator-0.4.0-linux-x64.tar.gz), where the program is marked as one that can be run. Each has a folder
# "BlyantValidator" with the program, README.md, CHANGELOG.md, CREDITS.md, THIRD-PARTY-NOTICES.md and LICENSE.
# The tests are run first (nothing is published when one fails), and the version must be the same in the places it
# is told: Version in Directory.Build.props, and the status line of README.md.
# The archives of a version that is published again are replaced, also those of an earlier version.
# Usage:  ./publish.ps1                     (all versions)
#         ./publish.ps1 portable,win-x64    (one or more versions)
#         ./publish.ps1 -SkipTests          (without running the tests first)
param(
    [string[]]$Platforms = @("portable", "win-x64", "win-arm64", "linux-x64", "linux-arm64", "linux-arm", "osx-x64", "osx-arm64"),
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src/Blyant.Validator.Cli/Blyant.Validator.Cli.csproj"
$output = Join-Path $PSScriptRoot "publish"
$release = Join-Path $output "release"
$known = @("portable", "win-x64", "win-arm64", "linux-x64", "linux-arm64", "linux-arm", "osx-x64", "osx-arm64")
$program = "blyant-validator"
$folderName = "BlyantValidator"
# The files that go with the program in every archive (LICENSE and THIRD-PARTY-NOTICES.md: the licences must go with
# every copy)
$documents = @("README.md", "CHANGELOG.md", "CREDITS.md", "THIRD-PARTY-NOTICES.md", "LICENSE")

# "portable,win-x64" as one argument (as when started with pwsh -File) is a list too
$Platforms = @($Platforms | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($platform in $Platforms) {
    if ($known -notcontains $platform) { throw "Unknown version: '$platform' (known: $($known -join ', '))" }
}

foreach ($document in $documents) {
    if (-not (Test-Path (Join-Path $PSScriptRoot $document))) { throw "$document is missing: nothing was published" }
}

# The version (0.4.0), for the names of the archives: the same in Directory.Build.props and in README.md
[xml]$props = Get-Content (Join-Path $PSScriptRoot "Directory.Build.props")
$version = $props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
$readme = [regex]::Match((Get-Content (Join-Path $PSScriptRoot "README.md") -Raw), 'Status: [^(]*\(([0-9][^)]*)\)').Groups[1].Value
if (-not $version -or $readme -ne $version) {
    throw "The version is not the same everywhere: Directory.Build.props $version, README.md $readme"
}

if (-not $SkipTests) {
    Write-Host "Running the tests..."
    dotnet test (Join-Path $PSScriptRoot "BlyantValidator.slnx") --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "The tests failed: nothing was published" }
}

# Each folder is emptied first, so files left from earlier versions are not released. A data folder (made when a
# version was tried there) holds the settings: it is never deleted, nothing is published. All folders are checked
# before any is emptied
foreach ($platform in $Platforms) {
    if (Test-Path (Join-Path $output $platform "data")) {
        throw "$(Join-Path $output $platform) contains a data folder: move it away first (nothing was deleted)"
    }
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Pack the files of a version for a release, in a folder "BlyantValidator": a tar.gz for Linux and macOS (the program
# marked as one that can be run, which a zip made on Windows cannot tell), else a zip
function New-ReleaseArchive([string]$platform, [string]$folder) {
    New-Item -ItemType Directory -Force $release | Out-Null
    $files = Get-ChildItem $folder -Recurse -File
    if ($platform -like "linux-*" -or $platform -like "osx-*") {
        $archive = Join-Path $release "$folderName-$version-$platform.tar.gz"
        if (Test-Path $archive) { Remove-Item $archive }
        $stream = [System.IO.File]::Create($archive)
        try {
            $gzip = [System.IO.Compression.GZipStream]::new($stream, [System.IO.Compression.CompressionLevel]::Optimal)
            $tar = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $false)
            foreach ($file in $files) {
                $name = [System.IO.Path]::GetRelativePath($folder, $file.FullName).Replace('\', '/')
                $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$folderName/$name")
                # rwxr-xr-x for the program, rw-r--r-- for the others
                $entry.Mode = [System.IO.UnixFileMode]$(if ($name -eq $program) { 493 } else { 420 })
                $entry.ModificationTime = [DateTimeOffset]$file.LastWriteTimeUtc
                $data = $file.OpenRead()
                try {
                    $entry.DataStream = $data
                    $tar.WriteEntry($entry)
                }
                finally { $data.Dispose() }
            }
            $tar.Dispose()
            $gzip.Dispose()
        }
        finally { $stream.Dispose() }
    }
    else {
        $archive = Join-Path $release "$folderName-$version-$platform.zip"
        if (Test-Path $archive) { Remove-Item $archive }
        $zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $name = [System.IO.Path]::GetRelativePath($folder, $file.FullName).Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, "$folderName/$name",
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $zip.Dispose() }
    }
}

foreach ($platform in $Platforms) {
    Write-Host "Publishing $platform..."
    $folder = Join-Path $output $platform
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
    # The archives of this version made before (also of an earlier version), so they are not released
    if (Test-Path $release) {
        Get-ChildItem $release -Filter "$folderName-*-$platform.*" -File | Remove-Item
    }
    if ($platform -eq "portable") {
        # (the project publishes a single file: not the portable version, which runs with "dotnet")
        dotnet publish $project -c Release `
            -p:PublishSingleFile=false `
            -p:UseAppHost=false `
            -p:DebugType=none `
            -o $folder --nologo -v quiet
    }
    else {
        dotnet publish $project -c Release -r $platform --self-contained true `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=none `
            -o $folder --nologo -v quiet
    }
    if ($LASTEXITCODE -ne 0) { throw "Publishing $platform failed" }
    foreach ($document in $documents) { Copy-Item (Join-Path $PSScriptRoot $document) $folder }
    New-ReleaseArchive $platform $folder
}

Get-ChildItem $release -File |
    Select-Object @{ n = "File"; e = { $_.Name } }, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize
