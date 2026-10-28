param([string]$Manifest = (Join-Path $PSScriptRoot '../avatar-assets.json'))
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$bundle=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$destination=Join-Path $workspace 'avatar-app/assets'
$complete=$true
foreach($asset in $bundle.files) {
    $path=Join-Path $destination $asset.path
    if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.sha256) {$complete=$false;break}
}
if($complete){Write-Host 'Recursos verificados.';return}
$download=Join-Path ([IO.Path]::GetTempPath()) ('avatar-assets-'+[guid]::NewGuid().ToString('N')+'.zip')
try {
    Invoke-WebRequest -Uri $bundle.url -OutFile $download
    if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $bundle.sha256){throw 'El paquete de recursos no coincide con su SHA256.'}
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Expand-Archive -LiteralPath $download -DestinationPath $destination -Force
    foreach($asset in $bundle.files) {
        $path=Join-Path $destination $asset.path
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.sha256){throw ('Recurso no válido: '+$asset.path)}
    }
} finally {if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download -Force}}
