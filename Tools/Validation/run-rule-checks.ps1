# Requires PowerShell 7. Uses its bundled Roslyn compiler; no SDK download or Unity installation.
# API substitutes cannot validate Unity, IL2CPP, Vuforia compatibility, or actual scene serialization.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scriptRoot = Join-Path $projectRoot 'ARPet/Assets/_Project/Scripts'
$outputRoot = Join-Path $PSScriptRoot ('Temp/run_' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$previousValidationRoot = $env:ARPET_VALIDATION_ROOT
$env:ARPET_VALIDATION_ROOT = $outputRoot

Add-Type -TypeDefinition 'public static class ARPetRoslynLoader { }'
$standardReferences = [Microsoft.CodeAnalysis.MetadataReference[]]@(
    Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object {
        [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_.FullName)
    }
)
$compiled = [Collections.Generic.List[object]]::new()

function Build-Assembly {
    param([string]$Name, [string[]]$Sources, [string[]]$References, [string[]]$Defines)
    $parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
        [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp9).WithPreprocessorSymbols($Defines)
    $trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@(
        foreach ($source in $Sources) {
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
                [IO.File]::ReadAllText($source), $parseOptions, $source, [Text.Encoding]::UTF8)
        }
    )
    $metadataReferences = [Microsoft.CodeAnalysis.MetadataReference[]]@(
        $standardReferences
        foreach ($reference in $References) { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($reference) }
    )
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
        [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($Name, $trees, $metadataReferences, $options)
    $destination = Join-Path $outputRoot ($Name + '.dll')
    $stream = [IO.File]::Create($destination)
    try { $emitted = $compilation.Emit($stream) }
    finally { $stream.Dispose() }
    $errors = @($emitted.Diagnostics | Where-Object Severity -EQ 'Error' | ForEach-Object ToString)
    $compiled.Add([PSCustomObject]@{ Assembly = $Name; Sources = $Sources.Count; Errors = $errors })
    if (!$emitted.Success) { throw ($errors -join [Environment]::NewLine) }
    return $destination
}

try {
    $engine = Build-Assembly 'ARPet.Validation.UnityApiStubs' @((Join-Path $PSScriptRoot 'UnityApiStubs.cs')) @() @()
    $editor = Build-Assembly 'ARPet.Validation.EditorApiStubs' @((Join-Path $PSScriptRoot 'EditorApiStubs.cs')) @($engine) @()
    $vuforia = Build-Assembly 'ARPet.Validation.VuforiaApiStubs' @((Join-Path $PSScriptRoot 'VuforiaApiStubs.cs')) @($engine) @()
    $nunit = Build-Assembly 'ARPet.Validation.NUnitShim' @((Join-Path $PSScriptRoot 'NUnitShim.cs')) @($engine) @()
    $runner = Build-Assembly 'ARPet.Validation.RuleRunner' @((Join-Path $PSScriptRoot 'RuleRunner.cs')) @($engine, $nunit) @()

    $folders = @('Core', 'Pet', 'Interaction', 'AR', 'Presentation', 'Platform', 'UI', 'Persistence', 'App', 'Editor', 'Tests/EditMode')
    $definitions = @{}
    foreach ($folder in $folders) {
        $directory = Join-Path $scriptRoot $folder
        $definitionPath = @(Get-ChildItem -LiteralPath $directory -Filter '*.asmdef')
        if ($definitionPath.Count -ne 1) { throw "Expected one asmdef in $directory" }
        $definition = Get-Content -LiteralPath $definitionPath[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $definitions[$folder] = $definition
    }

    $assemblies = @{}
    foreach ($folder in $folders) {
        $definition = $definitions[$folder]
        $projectReferences = @($definition.references | Where-Object { $_ -like 'ZQXNXS.ARPet.*' } | ForEach-Object {
            if (!$assemblies.ContainsKey($_)) { throw "Unresolved assembly reference: $_" }
            $assemblies[$_]
        })
        $references = @($engine) + $projectReferences
        if ($definition.includePlatforms -contains 'Editor') { $references += $editor }
        if ($folder -eq 'Tests/EditMode') { $references += $nunit }
        $sources = [string[]]@(Get-ChildItem -LiteralPath (Join-Path $scriptRoot $folder) -Filter '*.cs' | Select-Object -ExpandProperty FullName)
        $assemblies[$definition.name] = Build-Assembly $definition.name $sources $references @('UNITY_EDITOR', 'UNITY_INCLUDE_TESTS')
    }

    # Recompile Android and Vuforia conditional paths against API stand-ins, honoring project dependencies.
    $deviceAssemblies = @{}
    foreach ($folder in $folders | Where-Object { $_ -ne 'Tests/EditMode' }) {
        $definition = $definitions[$folder]
        $references = @($engine, $vuforia) + @($definition.references | Where-Object { $_ -like 'ZQXNXS.ARPet.*' } | ForEach-Object { $deviceAssemblies[$_] })
        $defines = @('UNITY_ANDROID')
        foreach ($versionDefine in $definition.versionDefines) {
            if ($versionDefine.name -ne 'com.ptc.vuforia.engine') {
                throw "Unexpected Vuforia package presence condition in $folder"
            }
            if ([string]::IsNullOrWhiteSpace($versionDefine.define)) {
                throw "Empty define in versionDefines for $folder"
            }
            # Unity 的版本表达式解析器不接受上界为空的方括号范围。
            # 2026-09-16 实测：写成 '[0.0.0,)' 时 Unity 抛
            # ExpressionNotValidException: '[0.0.0,)' is not a valid expression，
            # 宏不会定义，VuforiaTrackingSource 的真实实现会一直被条件编译排除。
            # 本检查无法复现 Unity 的解析器，只能拦住这种已知非法写法；
            # 表达式是否真正生效仍须以真实 Unity 编译日志为准。
            if ($versionDefine.expression -match '^[\[\(].*,\s*[\]\)]$') {
                throw ("versionDefines expression '{0}' in {1} uses an empty version bound, " -f $versionDefine.expression, $folder) +
                      "which Unity rejects with ExpressionNotValidException. Use a bare minimum version such as '0.0.0'."
            }
            $defines += $versionDefine.define
        }
        if ($folder -eq 'Editor') { $references += $editor; $defines += 'UNITY_EDITOR' }
        $sources = [string[]]@(Get-ChildItem -LiteralPath (Join-Path $scriptRoot $folder) -Filter '*.cs' | Select-Object -ExpandProperty FullName)
        $deviceAssemblies[$definition.name] = Build-Assembly ($definition.name + '.DeviceCheck') $sources $references $defines
    }

    [Reflection.Assembly]::LoadFrom($engine) | Out-Null
    [Reflection.Assembly]::LoadFrom($nunit) | Out-Null
    [Reflection.Assembly]::LoadFrom($runner) | Out-Null
    $testAssembly = [Reflection.Assembly]::LoadFrom($assemblies['ZQXNXS.ARPet.Tests.EditMode'])
    $results = @([ARPet.Validation.RuleRunner]::Run($testAssembly))
    $integrationSource = @((Join-Path $PSScriptRoot 'IntegrationChecks.cs'))
    $integrationAssembly = Build-Assembly 'ARPet.Validation.IntegrationChecks' $integrationSource (@($engine, $editor, $nunit) + @($assemblies.Values)) @('UNITY_EDITOR')
    $deviceIntegrationAssembly = Build-Assembly 'ARPet.Validation.DeviceChecks' $integrationSource (@($engine, $editor, $vuforia, $nunit) + @($deviceAssemblies.Values)) @('VALIDATION_DEVICE')
    [Reflection.Assembly]::LoadFrom($editor) | Out-Null
    [Reflection.Assembly]::LoadFrom($vuforia) | Out-Null
    $integrationResults = @([ARPet.Validation.RuleRunner]::Run([Reflection.Assembly]::LoadFrom($integrationAssembly))) +
        @([ARPet.Validation.RuleRunner]::Run([Reflection.Assembly]::LoadFrom($deviceIntegrationAssembly)))
    $failed = @(@($results) + @($integrationResults) | Where-Object { !$_.Passed })
    $report = [PSCustomObject]@{
        Scope = 'Offline Roslyn C# 9 compilation with API stubs and reflection-run rule tests. Not Unity/NUnit/IL2CPP or device validation.'
        Timestamp = [DateTimeOffset]::Now.ToString('o')
        CompilerHost = $PSVersionTable.PSVersion.ToString()
        Assemblies = $compiled
        RuleTestCount = $results.Count
        IntegrationCheckCount = $integrationResults.Count
        Passed = $results.Count + $integrationResults.Count - $failed.Count
        Failed = $failed.Count
        Results = $results
        IntegrationResults = $integrationResults
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'latest-results.json') -Encoding UTF8
    Write-Output ("Offline compilation: {0} assemblies. Rules: {1}; stub integration checks: {2}. Total: {3} passed, {4} failed." -f $compiled.Count, $results.Count, $integrationResults.Count, $report.Passed, $report.Failed)
    foreach ($failure in $failed) { Write-Output ($failure.Name + [Environment]::NewLine + $failure.Error) }
    if ($failed.Count -gt 0) { exit 1 }
}
finally {
    $env:ARPET_VALIDATION_ROOT = $previousValidationRoot
}
