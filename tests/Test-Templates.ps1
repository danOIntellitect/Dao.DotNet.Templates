[CmdletBinding()]
param(
    [ValidateSet('Generate', 'Build', 'Test')]
    [string] $Stage = 'Test',
    [switch] $AllCombinations,
    [string] $ArtifactsDirectory = (Join-Path (Split-Path $PSScriptRoot) 'artifacts'),
    [string[]] $Templates = @('Angular', 'Avalonia', 'Ef', 'Expo', 'Mcp', 'Sql')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$generated = Join-Path $artifacts 'generated'
$hive = Join-Path $artifacts 'template-hive'
$packageOutput = Join-Path $artifacts 'packages'
$allTemplates = @('Angular', 'Avalonia', 'Ef', 'Expo', 'Mcp', 'Sql')

if (@($Templates | Where-Object { $_ -notin $allTemplates }).Count -gt 0) {
    throw "Unknown template selection. Choose from: $($allTemplates -join ', ')."
}

if ($Stage -eq 'Test' -and @($allTemplates | Where-Object { $_ -notin $Templates }).Count -gt 0) {
    throw 'The Test stage requires all six templates. Use the Build stage for a selected template.'
}

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-GeneratedProject {
    param([string] $Directory, [string] $SourceName)
    $files = Get-ChildItem $Directory -Recurse -File
    foreach ($file in $files) {
        if ($file.FullName -match '[\\/](bin|obj|node_modules|\.template\.config)[\\/]') {
            throw "Generated output contains excluded content: $($file.FullName)"
        }
        if ($file.Extension -in '.cs', '.csproj', '.slnx', '.ts', '.js') {
            $content = Get-Content $file.FullName -Raw
            if ($content.Contains($SourceName)) {
                throw "Source name was not replaced in $($file.FullName)."
            }
            if ($content -match '(?m)^\s*(#if|//#if|<!--#if).*(Include|Require|Google|MSAL)') {
                throw "Unprocessed template condition in $($file.FullName)."
            }
        }
        if ($file.Extension -eq '.csproj') {
            [xml] $project = Get-Content $file.FullName -Raw
            foreach ($reference in $project.SelectNodes('//ProjectReference')) {
                $relative = $reference.Include.Replace('\', [IO.Path]::DirectorySeparatorChar)
                $target = Join-Path $file.DirectoryName $relative
                if (-not (Test-Path $target)) {
                    throw "Dangling project reference in $($file.FullName): $relative"
                }
            }
        }
    }
}

function New-SmokeProject {
    param([string] $ShortName, [string] $Name, [string] $SourceName, [string[]] $Options = @())
    $destination = Join-Path $generated $Name
    if (Test-Path $destination) {
        throw "Output already exists: $destination. Use a fresh checkout or remove this specific generated project before rerunning."
    }
    Invoke-Checked dotnet (@('new', $ShortName, '-n', $Name, '-o', $destination,
        '--debug:custom-hive', $hive) + $Options)
    Assert-GeneratedProject $destination $SourceName
    return $destination
}

New-Item $packageOutput -ItemType Directory -Force | Out-Null
Invoke-Checked dotnet @('pack', (Join-Path $root 'Dao.Templates.csproj'), '-c', 'Release', '-o', $packageOutput)
$package = Get-ChildItem $packageOutput -Filter 'Dao.Templates.*.nupkg' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
Invoke-Checked dotnet @('new', 'install', $package.FullName, '--debug:custom-hive', $hive, '--force')

$definitions = @(
    @{ Key = 'Angular'; Short = 'dao-aspire-angular'; Name = 'SmokeAngular'; Source = 'Dao.Aspire.Angular' },
    @{ Key = 'Avalonia'; Short = 'dao-aspire-avalonia'; Name = 'SmokeAvalonia'; Source = 'Dao.Aspire.Avalonia' },
    @{ Key = 'Ef'; Short = 'dao-aspire-ef'; Name = 'SmokeEf'; Source = 'Dao.Aspire.Ef' },
    @{ Key = 'Expo'; Short = 'dao-aspire-expo'; Name = 'SmokeExpo'; Source = 'Dao.Aspire.Expo' },
    @{ Key = 'Mcp'; Short = 'dao-aspire-mcp'; Name = 'SmokeMcp'; Source = 'Dao.Aspire.Mcp' },
    @{ Key = 'Sql'; Short = 'dao-sql-mcp'; Name = 'SmokeSql'; Source = 'Dao.Sql.Mcp' }
)

$outputs = [Collections.Generic.List[string]]::new()
foreach ($definition in $definitions | Where-Object { $_.Key -in $Templates }) {
    $parameters = @{
        ShortName = $definition.Short
        Name = $definition.Name
        SourceName = $definition.Source
    }
    $destination = New-SmokeProject @parameters
    $outputs.Add($destination)
}

if ('Angular' -in $Templates) {
    $outputs.Add((New-SmokeProject 'dao-aspire-angular' 'SmokeSignalR' 'Dao.Aspire.Angular' @('--IncludeSignalR', 'true')))
    foreach ($auth in @('GoogleCookie', 'GoogleJwt', 'MSAL')) {
        foreach ($signalR in @($false, $true)) {
            $name = "Smoke$auth$(if ($signalR) { 'SignalR' })"
            $destination = New-SmokeProject 'dao-aspire-angular' $name 'Dao.Aspire.Angular' @(
                '--Auth', $auth, '--IncludeSignalR', $signalR.ToString().ToLowerInvariant())
            $outputs.Add($destination)
            $manifestPath = Join-Path $destination "$name.Web\package.json"
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            $lock = Get-Content (Join-Path $destination "$name.Web\package-lock.json") -Raw | ConvertFrom-Json -AsHashtable
            if ($manifest.dependencies.'oidc-client-ts' -and $auth -ne 'GoogleJwt') {
                throw "OIDC client leaked into $auth output."
            }
            if ($manifest.dependencies.'@azure/msal-angular' -and $auth -ne 'MSAL') {
                throw "MSAL leaked into $auth output."
            }
            foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
                if ($lock.packages[''].dependencies[$dependency.Name] -ne $dependency.Value) {
                    throw "Manifest/lockfile mismatch for $($dependency.Name) in $name."
                }
            }
            if (Test-Path (Join-Path $destination 'auth-assets')) {
                throw "Template-only auth assets leaked into $name."
            }
            if (-not (Test-Path (Join-Path $destination 'docs\authentication.md'))) {
                throw "Missing provider setup documentation in $name."
            }
        }
    }
}

if ('Mcp' -in $Templates) {
    $options = @('IncludeAuthentication', 'IncludeWebClient', 'IncludeDatabase',
        'IncludeRoleBasedAuth', 'IncludeSampleData', 'IncludeMcpInspector', 'IncludeApplicationInsights')
    # The full binary enumeration also contains every pairwise and boundary case.
    $masks = if ($AllCombinations) { 0..127 } else { @(0, 1, 2, 4, 8, 16, 22, 32, 64, 42, 85, 63, 95, 111, 119, 123, 125, 126) }
    foreach ($mask in $masks) {
        $arguments = foreach ($index in 0..6) {
            "--$($options[$index])"
            (($mask -band (1 -shl $index)) -ne 0).ToString().ToLowerInvariant()
        }
        $destination = New-SmokeProject 'dao-aspire-mcp' "SmokeMcp$mask" 'Dao.Aspire.Mcp' $arguments
        $outputs.Add($destination)
    }
}

if ($Stage -eq 'Generate') { return }

foreach ($destination in $outputs) {
    $solution = Get-ChildItem $destination -Filter '*.slnx' | Select-Object -First 1
    Invoke-Checked dotnet @('build', $solution.FullName, '-c', 'Release', '-p:EnableWindowsTargeting=true')
    foreach ($testProject in Get-ChildItem $destination -Recurse -Filter '*.Tests.csproj') {
        Invoke-Checked dotnet @('test', $testProject.FullName, '-c', 'Release', '--no-build',
            '--filter', 'Category!=Agentic')
    }
    foreach ($manifest in Get-ChildItem $destination -Filter package.json -Recurse) {
        Push-Location $manifest.DirectoryName
        try {
            Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
            if ($manifest.DirectoryName -match 'Web$') {
                Invoke-Checked npm @('run', 'build')
                Invoke-Checked npm @('test', '--', '--watch=false')
            } else {
                Invoke-Checked npm @('run', 'typecheck')
                Invoke-Checked npm @('test')
                Invoke-Checked npx @('expo-doctor')
            }
        } finally {
            Pop-Location
        }
    }
}

$generatedRootArgument = "-p:GeneratedRoot=$generated"
if ($Stage -eq 'Test' -and $IsWindows) {
    $desktopDirectory = Join-Path $PSScriptRoot 'Template.DesktopTests'
    $desktopProject = Join-Path $desktopDirectory 'Template.DesktopTests.csproj'
    Invoke-Checked dotnet @('test', $desktopProject, '-c', 'Release', $generatedRootArgument,
        '-p:EnableWindowsTargeting=true', '--logger', 'trx',
        '--results-directory', (Join-Path $artifacts 'TestResults'))
}

if ($Stage -eq 'Test') {
    $authenticationProject = Join-Path $PSScriptRoot 'Template.AuthenticationTests\Template.AuthenticationTests.csproj'
    Invoke-Checked dotnet @('test', $authenticationProject, '-c', 'Release', $generatedRootArgument)
    $integrationDirectory = Join-Path $PSScriptRoot 'Template.IntegrationTests'
    $integrationProject = Join-Path $integrationDirectory 'Template.IntegrationTests.csproj'
    Invoke-Checked dotnet @('build', $integrationProject, '-c', 'Release', $generatedRootArgument,
        '-p:EnableWindowsTargeting=true')

    $testOutputDirectory = Join-Path (Join-Path (Join-Path $integrationDirectory 'bin') 'Release') 'net10.0'
    $playwrightInstaller = Join-Path $testOutputDirectory 'playwright.ps1'
    if (-not (Test-Path $playwrightInstaller)) {
        throw "Playwright installer not found: $playwrightInstaller"
    }
    $playwrightArguments = @('-NoProfile', '-File', $playwrightInstaller, 'install')
    if ($IsLinux) { $playwrightArguments += '--with-deps' }
    $playwrightArguments += 'chromium'
    Invoke-Checked pwsh $playwrightArguments

    Invoke-Checked dotnet @('test', $integrationProject, '-c', 'Release', '--no-build',
        $generatedRootArgument, '--logger', 'trx', '--results-directory', (Join-Path $artifacts 'TestResults'))
}
