<#
.SYNOPSIS
    Builds, installs, configures, verifies and uninstalls the PowerToys Run Translate plugin.

.DESCRIPTION
    One entry point for everything. Running it with no switches does the whole job:

        build -> copy into the PowerToys Run plugin folder -> ask for credentials (first time only)
        -> restart PowerToys Run -> verify registration

    It is idempotent, so running it again is the way to update after pulling changes.

    Actions:
        (default)       install or update, and configure on a first install
        -Reconfigure    ask for the provider and credentials again
        -Show           print the current configuration and exit
        -Uninstall      remove the plugin

    Notable behaviour:
        * Only the PowerToys Run module process is restarted, never the PowerToys runner. The module
          is what loads plugins and registers the Alt+Space hotkey, and the runner is often running
          elevated, in which case a non-elevated shell cannot stop it at all.
        * An existing config.json is never overwritten by an install; use -Reconfigure to change it.
        * config.json lives in the plugin folder because PowerToys 0.64 can only render boolean
          plugin options, so an API key cannot be entered in the PowerToys settings UI.

    This script is deliberately ASCII-only. Windows PowerShell 5.1 decodes a .ps1 file using the
    system code page unless it carries a UTF-8 BOM, which turns non-ASCII literals into mojibake and
    can even break parsing. Keep it ASCII, or re-add the BOM after editing.

.PARAMETER PowerToysPath
    PowerToys install directory. Used for build references and for locating the module to restart.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Show
    Print the current configuration (secret masked) and exit.

.PARAMETER Reconfigure
    Ask for provider, default target and credentials even when config.json already exists.

.PARAMETER Uninstall
    Remove the plugin. By default PowerToys deletes the folder at its next start, driven by a
    NeedDelete.txt marker, which avoids deleting files a running process still has open.

.PARAMETER Immediate
    With -Uninstall: delete the folder now instead of writing the marker.

.PARAMETER Yes
    With -Uninstall: skip the confirmation prompt.

.PARAMETER SkipBuild
    Deploy the existing build output without running dotnet build.

.PARAMETER SkipRestart
    Copy files but do not restart PowerToys Run. Registration cannot be verified in that case.

.PARAMETER NonInteractive
    Never prompt. Missing values keep their current value and a missing config.json is written with
    defaults, which also makes this script safe to call from other scripts.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1 -Show

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$PowerToysPath = 'C:\Program Files\PowerToys',
    [string]$Configuration = 'Release',
    [switch]$Show,
    [switch]$Reconfigure,
    [switch]$Uninstall,
    [switch]$Immediate,
    [switch]$Yes,
    [switch]$SkipBuild,
    [switch]$SkipRestart,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Constants and paths
# ---------------------------------------------------------------------------------------------

$PluginId = 'FEC38B56721B49B888D221913E65C853'
$PluginFolderName = 'Translate'
$AssemblyName = 'Community.PowerToys.Run.Plugin.Translate'
$RepoRoot = $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\$AssemblyName\$AssemblyName.csproj"
$OutputDir = Join-Path $RepoRoot "src\$AssemblyName\bin\$Configuration"

function Get-LauncherModulePath {
    return (Join-Path $PowerToysPath 'modules\launcher')
}

<#
    Mirrors Wox.Plugin.Constant.DetermineDataDirectory: portable mode is active when the launcher
    module folder contains a UserData directory, and then the plugin folder and settings.json live
    under it rather than under LOCALAPPDATA.
#>
function Get-PowerToysRunPaths {
    $portableRoot = Join-Path (Get-LauncherModulePath) 'UserData'

    if (Test-Path -LiteralPath $portableRoot) {
        return @{
            Root     = $portableRoot
            Plugins  = Join-Path $portableRoot 'Plugins'
            Settings = Join-Path $portableRoot 'settings.json'
            Portable = $true
        }
    }

    $root = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerToys Run'
    return @{
        Root     = $root
        Plugins  = Join-Path $root 'Plugins'
        Settings = Join-Path $root 'settings.json'
        Portable = $false
    }
}

function Get-PluginInstallDir {
    $paths = Get-PowerToysRunPaths
    return (Join-Path $paths.Plugins $PluginFolderName)
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $encoding = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

# ---------------------------------------------------------------------------------------------
# Configuration file handling
# ---------------------------------------------------------------------------------------------

function New-DefaultConfigObject {
    return [pscustomobject][ordered]@{
        provider              = 'baidu'
        fallbackProvider      = 'none'
        defaultTarget         = 'zh'
        autoDirection         = $true
        requestTimeoutSeconds = 8
        minIntervalMs         = 1100
        debounceMs            = 400
        maxCharsPerRequest    = 1900
        baidu                 = [pscustomobject][ordered]@{ appid = ''; secretKey = '' }
    }
}

function Read-ConfigObject {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return (New-DefaultConfigObject)
    }

    try {
        return ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json)
    }
    catch {
        Write-Warning 'Existing config.json is not valid JSON; rebuilding it from defaults.'
        return (New-DefaultConfigObject)
    }
}

function Get-ConfigValue {
    param($Config, [string]$Name, $Default)

    $existing = $Config.PSObject.Properties[$Name]
    if ($existing -and $null -ne $existing.Value) {
        return $existing.Value
    }

    return $Default
}

function Set-ConfigValue {
    param(
        [Parameter(Mandatory = $true)]$Config,
        [Parameter(Mandatory = $true)][string]$Name,
        $Value
    )

    $existing = $Config.PSObject.Properties[$Name]
    if ($existing) {
        $existing.Value = $Value
    }
    else {
        $Config | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}

function Get-Masked {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return '(empty)'
    }

    if ($Value.Length -le 4) {
        return '****'
    }

    return $Value.Substring(0, 4) + '****'
}

<#
    Prompts unless -NonInteractive, in which case the current value is kept. An empty answer also
    keeps the current value, so pressing Enter never destroys existing settings.
    AllowEmptyString is required: "no appid configured yet" is an empty string, and a Mandatory
    parameter rejects that with a parameter binding error.
#>
function Read-Choice {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Current
    )

    if ($NonInteractive) {
        return $Current
    }

    $answer = Read-Host "$Prompt (current: $Current)"
    if ([string]::IsNullOrWhiteSpace($answer)) {
        return $Current
    }

    return $answer.Trim()
}

<#
    Reads the secret without echoing it, then converts it to plain text because it has to be written
    into config.json anyway. Falls back to a visible prompt if the marshal path is unavailable.
#>
function Read-SecretPlainText {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    try {
        $secure = Read-Host -Prompt $Prompt -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    catch {
        return (Read-Host -Prompt $Prompt)
    }
}

function Assert-Choice {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][string[]]$Allowed,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notin $Allowed) {
        throw "Unsupported $Name '$Value'. Use one of: $($Allowed -join ', ')."
    }
}

function Show-Config {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "No config.json yet at $Path" -ForegroundColor Yellow
        Write-Host 'Run install.ps1 first (it will create one and ask for credentials).'
        return
    }

    $config = Read-ConfigObject -Path $Path
    if (-not $config.PSObject.Properties['baidu']) {
        $config | Add-Member -NotePropertyName baidu -NotePropertyValue ([pscustomobject][ordered]@{ appid = ''; secretKey = '' })
    }

    Write-Host "Config file      : $Path"
    Write-Host "provider         : $(Get-ConfigValue -Config $config -Name 'provider' -Default 'baidu')"
    Write-Host "fallbackProvider : $(Get-ConfigValue -Config $config -Name 'fallbackProvider' -Default 'none')"
    Write-Host "defaultTarget    : $(Get-ConfigValue -Config $config -Name 'defaultTarget' -Default 'zh')"
    Write-Host "autoDirection    : $(Get-ConfigValue -Config $config -Name 'autoDirection' -Default $true)"
    Write-Host "appid            : $(Get-ConfigValue -Config $config.baidu -Name 'appid' -Default '(empty)')"
    Write-Host "secretKey        : $(Get-Masked (Get-ConfigValue -Config $config.baidu -Name 'secretKey' -Default ''))"
}

<#
    Merges provider, target and credentials into config.json. 'Baidi console' prompts only appear
    when Baidu is actually the primary or fallback provider.
#>
function Invoke-Configure {
    param([Parameter(Mandatory = $true)][string]$Path)

    $config = Read-ConfigObject -Path $Path
    if (-not $config.PSObject.Properties['baidu']) {
        $config | Add-Member -NotePropertyName baidu -NotePropertyValue ([pscustomobject][ordered]@{ appid = ''; secretKey = '' })
    }

    Write-Host "Config file: $Path"

    $provider = Read-Choice -Prompt 'Translation provider [baidu/google/none]' `
        -Current (Get-ConfigValue -Config $config -Name 'provider' -Default 'baidu')
    $provider = $provider.Trim().ToLowerInvariant()
    Assert-Choice -Value $provider -Allowed @('baidu', 'google', 'none') -Name 'provider'
    Set-ConfigValue -Config $config -Name 'provider' -Value $provider

    $fallback = Read-Choice -Prompt 'Fallback provider [none/baidu/google]' `
        -Current (Get-ConfigValue -Config $config -Name 'fallbackProvider' -Default 'none')
    $fallback = $fallback.Trim().ToLowerInvariant()
    Assert-Choice -Value $fallback -Allowed @('none', 'baidu', 'google') -Name 'fallbackProvider'
    Set-ConfigValue -Config $config -Name 'fallbackProvider' -Value $fallback

    $target = Read-Choice -Prompt 'Default target language, for example zh/en/jp' `
        -Current (Get-ConfigValue -Config $config -Name 'defaultTarget' -Default 'zh')
    Set-ConfigValue -Config $config -Name 'defaultTarget' -Value $target.Trim()

    $appId = Get-ConfigValue -Config $config.baidu -Name 'appid' -Default ''
    $secretKey = Get-ConfigValue -Config $config.baidu -Name 'secretKey' -Default ''

    if ($provider -eq 'baidu' -or $fallback -eq 'baidu') {
        Write-Host ''
        Write-Host 'Baidu console: https://fanyi-api.baidu.com/ -> developer information' -ForegroundColor Cyan

        $appId = Read-Choice -Prompt 'Baidu appid' -Current $appId

        if (-not $NonInteractive) {
            $answer = Read-SecretPlainText -Prompt 'Baidu secretKey (not echoed; leave empty to keep the current value)'
            if (-not [string]::IsNullOrWhiteSpace($answer)) {
                $secretKey = $answer.Trim()
            }
        }

        Set-ConfigValue -Config $config.baidu -Name 'appid' -Value $appId.Trim()
        Set-ConfigValue -Config $config.baidu -Name 'secretKey' -Value $secretKey.Trim()
    }

    Write-Utf8NoBom -Path $Path -Content ($config | ConvertTo-Json -Depth 6)

    Write-Host ''
    Write-Host 'Configuration written.' -ForegroundColor Green
    Write-Host "  provider         : $provider"
    Write-Host "  fallbackProvider : $fallback"
    Write-Host "  defaultTarget    : $target"
    Write-Host "  appid            : $appId"
    Write-Host "  secretKey        : $(Get-Masked $secretKey)"
    Write-Host ''
    Write-Host 'Note: the secret is stored as plain text in that config.json. Do not share the file.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------------------------
# PowerToys Run process handling
# ---------------------------------------------------------------------------------------------

function Stop-PowerToysRun {
    $launcher = Get-Process -Name 'PowerToys.PowerLauncher' -ErrorAction SilentlyContinue
    if (-not $launcher) {
        return
    }

    Write-Host 'Stopping PowerToys Run ...' -ForegroundColor Cyan
    $launcher | Stop-Process -Force -ErrorAction SilentlyContinue

    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name 'PowerToys.PowerLauncher' -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    Write-Warning 'PowerToys Run did not exit within 15 seconds.'
}

function Start-PowerToysRunAndWait {
    $launcherExe = Join-Path (Get-LauncherModulePath) 'PowerToys.PowerLauncher.exe'
    if (-not (Test-Path -LiteralPath $launcherExe)) {
        $launcherExe = Join-Path $PowerToysPath 'PowerToys.exe'
        if (-not (Test-Path -LiteralPath $launcherExe)) {
            Write-Warning "Neither PowerToys.PowerLauncher.exe nor PowerToys.exe was found under $PowerToysPath."
            return $false
        }
    }

    Write-Host "Starting $(Split-Path -Leaf $launcherExe) ..." -ForegroundColor Cyan
    Start-Process -FilePath $launcherExe -WorkingDirectory (Split-Path -Parent $launcherExe)

    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        if (Get-Process -Name 'PowerToys.PowerLauncher' -ErrorAction SilentlyContinue) {
            # Give the launcher a moment to finish writing settings.json.
            Start-Sleep -Seconds 2
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    Write-Warning 'PowerToys Run did not start within 60 seconds.'
    return $false
}

# The launcher rewrites settings.json while starting, so poll instead of reading once.
function Get-PluginSettingsEntry {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsFile,
        [int]$TimeoutSeconds = 15
    )

    if (-not (Test-Path -LiteralPath $SettingsFile)) {
        return $null
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $parsed = [System.IO.File]::ReadAllText($SettingsFile) | ConvertFrom-Json
            $entry = @($parsed.plugins) | Where-Object { $_.Id -eq $PluginId } | Select-Object -First 1
            if ($entry) {
                return $entry
            }
        }
        catch {
            # File is mid-write; try again.
        }

        Start-Sleep -Milliseconds 500
    }

    return $null
}

function Show-RelevantLogLines {
    param([Parameter(Mandatory = $true)][string]$LogRoot)

    if (-not (Test-Path -LiteralPath $LogRoot)) {
        return
    }

    $log = Get-ChildItem -Path $LogRoot -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $log) {
        return
    }

    Write-Host ''
    Write-Host "Recent plugin related log lines from $($log.FullName):" -ForegroundColor Cyan
    $found = Select-String -Path $log.FullName -Pattern 'Translate|FailedToInitialize|couldn''t load assembly|couldn''t get assembly types' -ErrorAction SilentlyContinue
    if ($found) {
        $found | Select-Object -Last 6 | ForEach-Object { Write-Host "  $($_.Line)" }
    }
    else {
        Write-Host '  (none)'
    }
}

# ---------------------------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------------------------

function Invoke-Build {
    Write-Host "Building $Configuration ..." -ForegroundColor Cyan
    & dotnet build $ProjectPath -c $Configuration "-p:PowerToysPath=$PowerToysPath" --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

function Copy-PluginFiles {
    param([Parameter(Mandatory = $true)][string]$Destination)

    if (-not (Test-Path -LiteralPath $OutputDir)) {
        throw "Build output not found: $OutputDir. Run again without -SkipBuild."
    }

    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    foreach ($file in @("$AssemblyName.dll", "$AssemblyName.pdb", 'plugin.json')) {
        $source = Join-Path $OutputDir $file
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $Destination -Force
        }
    }

    $imagesSource = Join-Path $OutputDir 'Images'
    if (Test-Path -LiteralPath $imagesSource) {
        $imagesDestination = Join-Path $Destination 'Images'
        if (-not (Test-Path -LiteralPath $imagesDestination)) {
            New-Item -ItemType Directory -Path $imagesDestination -Force | Out-Null
        }

        Copy-Item -Path (Join-Path $imagesSource '*.png') -Destination $imagesDestination -Force
    }
}

function Invoke-Install {
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project not found: $ProjectPath"
    }

    $woxPlugin = Join-Path (Get-LauncherModulePath) 'Wox.Plugin.dll'
    if (-not (Test-Path -LiteralPath $woxPlugin)) {
        throw "Wox.Plugin.dll not found at $woxPlugin. Pass -PowerToysPath if PowerToys is installed elsewhere."
    }

    $paths = Get-PowerToysRunPaths
    $installDir = Get-PluginInstallDir
    $configPath = Join-Path $installDir 'config.json'

    Write-Host "Plugin id      : $PluginId"
    Write-Host "Install folder : $installDir"
    if ($paths.Portable) {
        Write-Host 'Mode           : portable (UserData folder detected)' -ForegroundColor Yellow
    }

    if (-not $SkipBuild) {
        Invoke-Build
    }

    if (-not $SkipRestart) {
        Stop-PowerToysRun
    }

    Copy-PluginFiles -Destination $installDir

    # Ask for credentials on a first install, or whenever the user asks to reconfigure.
    $needsConfigure = $Reconfigure -or -not (Test-Path -LiteralPath $configPath)
    if ($needsConfigure -and -not $NonInteractive) {
        Write-Host ''
        Invoke-Configure -Path $configPath
    }
    elseif (-not (Test-Path -LiteralPath $configPath)) {
        Write-Utf8NoBom -Path $configPath -Content ((New-DefaultConfigObject) | ConvertTo-Json -Depth 6)
        Write-Host "Wrote default config: $configPath"
    }
    else {
        Write-Host "Keeping existing config: $configPath" -ForegroundColor DarkGray
    }

    if ($SkipRestart) {
        Write-Host ''
        Write-Host 'Files copied. PowerToys Run was not restarted (-SkipRestart), so the plugin is not loaded yet.' -ForegroundColor Yellow
        return
    }

    if (-not (Start-PowerToysRunAndWait)) {
        Write-Host ''
        Write-Host 'FAIL: PowerToys Run is not running, cannot verify.' -ForegroundColor Red
        Write-Host 'Start it from the PowerToys tray icon, or run:' -ForegroundColor Yellow
        Write-Host "  $(Join-Path (Get-LauncherModulePath) 'PowerToys.PowerLauncher.exe')"
        exit 1
    }

    $entry = Get-PluginSettingsEntry -SettingsFile $paths.Settings

    Write-Host ''
    if ($entry) {
        Write-Host 'PASS: the plugin is registered with PowerToys Run.' -ForegroundColor Green
        Write-Host "  Name          : $($entry.Name)"
        Write-Host "  ActionKeyword : $($entry.ActionKeyword)"
        Write-Host "  Disabled      : $($entry.Disabled)"
        Write-Host "  IsGlobal      : $($entry.IsGlobal)"
        Write-Host ''

        if ($entry.ActionKeyword -ne 'tr' -or $entry.Disabled) {
            Write-Host 'Note: the keyword is not "tr" or the plugin is disabled. Adjust it in the' -ForegroundColor Yellow
            Write-Host '      PowerToys settings, or delete the plugins entry in settings.json and rerun.' -ForegroundColor Yellow
        }

        Write-Host 'Open PowerToys Run (Alt+Space) and type:  tr hello' -ForegroundColor Green
        Show-RelevantLogLines -LogRoot (Join-Path $paths.Root 'Logs')
    }
    else {
        Write-Host 'FAIL: the plugin was not registered.' -ForegroundColor Red
        Write-Host "Checked: $($paths.Settings)"
        Show-RelevantLogLines -LogRoot (Join-Path $paths.Root 'Logs')
        exit 1
    }
}

# ---------------------------------------------------------------------------------------------
# Uninstall
# ---------------------------------------------------------------------------------------------

<#
    Guard rail: this action deletes a directory, so prove the resolved path really is the plugin
    folder (a 'Translate' leaf inside a 'Plugins' directory) before touching anything.
#>
function Resolve-SafeInstallDir {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ((Split-Path -Leaf $resolved) -ne $PluginFolderName) {
        throw "Refusing to use '$resolved': the last path segment is not '$PluginFolderName'."
    }

    if ((Split-Path -Leaf (Split-Path -Parent $resolved)) -ne 'Plugins') {
        throw "Refusing to use '$resolved': its parent folder is not named 'Plugins'."
    }

    return $resolved
}

function Remove-PluginFolderWithRetry {
    param([Parameter(Mandatory = $true)][string]$Path)

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return $true
        }
        catch {
            # A handle may still be closing; give it a moment and try again.
            Start-Sleep -Seconds 1
        }
    }

    return $false
}

function Write-NeedDeleteMarker {
    param([Parameter(Mandatory = $true)][string]$Path)

    Write-Utf8NoBom -Path (Join-Path $Path 'NeedDelete.txt') `
        -Content "This marker tells PowerToys Run to delete this folder on its next start.`r`n"
}

function Invoke-Uninstall {
    $myInstallDir = Resolve-SafeInstallDir -Path (Get-PluginInstallDir)
    $paths = Get-PowerToysRunPaths

    Write-Host "Plugin folder to remove : $myInstallDir"
    Write-Host "Settings file           : $($paths.Settings)"
    Write-Host ''

    if (-not (Test-Path -LiteralPath $myInstallDir)) {
        Write-Host 'Nothing to do: the plugin folder does not exist.' -ForegroundColor Yellow
        return
    }

    if (-not $Yes) {
        $answer = Read-Host 'Remove the plugin folder? Type y to continue'
        if ($answer -ne 'y' -and $answer -ne 'Y') {
            Write-Host 'Cancelled.'
            return
        }
    }

    # The plugin DLL stays locked for as long as PowerToys Run is running, so the module is stopped
    # first regardless of which removal strategy is used.
    if (-not $SkipRestart) {
        Stop-PowerToysRun
    }

    if ($Immediate) {
        if (Remove-PluginFolderWithRetry -Path $myInstallDir) {
            Write-Host "Deleted $myInstallDir" -ForegroundColor Green
        }
        else {
            Write-Warning 'Could not delete the folder (still locked). Falling back to the NeedDelete.txt marker.'
            Write-NeedDeleteMarker -Path $myInstallDir
            Write-Host 'Wrote NeedDelete.txt; PowerToys Run deletes the folder when it next starts.' -ForegroundColor Green
        }
    }
    else {
        Write-NeedDeleteMarker -Path $myInstallDir
        Write-Host 'Wrote NeedDelete.txt; PowerToys Run deletes the folder when it next starts.' -ForegroundColor Green
    }

    if ($SkipRestart) {
        Write-Host 'PowerToys Run was not restarted (-SkipRestart).' -ForegroundColor Yellow
        return
    }

    if (-not (Start-PowerToysRunAndWait)) {
        Write-Host 'FAIL: PowerToys Run did not restart, so removal could not be verified.' -ForegroundColor Red
        exit 1
    }

    Write-Host ''
    if (Test-Path -LiteralPath $myInstallDir) {
        Write-Host 'FAIL: the plugin folder still exists.' -ForegroundColor Red
        Write-Host "Delete it manually: $myInstallDir"
        exit 1
    }

    $entry = Get-PluginSettingsEntry -SettingsFile $paths.Settings
    Write-Host 'PASS: the plugin folder was removed.' -ForegroundColor Green
    if ($entry) {
        Write-Host 'Note: settings.json still lists the plugin; it is dropped on the next restart.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'PASS: the plugin is no longer registered either.' -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------------------------
# Dispatch
# ---------------------------------------------------------------------------------------------

if ($Show) {
    Show-Config -Path (Join-Path (Get-PluginInstallDir) 'config.json')
    return
}

if ($Uninstall) {
    Invoke-Uninstall
    return
}

# -Reconfigure does not need its own branch: Invoke-Install already forces the prompts when it is
# set, so the flow stays "one command that ends with a verified install".
Invoke-Install
