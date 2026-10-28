param([switch]$SkipTests,[switch]$SkipPublish,[string]$Version,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $workspace
[xml]$project=Get-Content -LiteralPath 'avatar-app/AvatarStudio.csproj'
$sourceVersion=[string]$project.Project.PropertyGroup.Version
if(!$Version){$Version=$sourceVersion}
if($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $sourceVersion){throw 'La versión debe coincidir con AvatarStudio.csproj (por ejemplo: 0.14.0).'}
& (Join-Path $PSScriptRoot 'fetch_avatar_assets.ps1')
$app=Join-Path $workspace ('artifacts/app-'+$Version)
$release=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $workspace ('dist/avatar-studio-'+$Version+'-installer')}
New-Item -ItemType Directory -Path $app,$release -Force | Out-Null
if(!$SkipPublish){
    dotnet publish avatar-app/AvatarStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $app
    if($LASTEXITCODE -ne 0){throw 'Falló la compilación.'}
}
$exe=Join-Path $app 'Axx-Belmont-Avatar-Studio.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'No se encontró el ejecutable compilado.'}
Copy-Item -LiteralPath avatar-app/LEEME.txt -Destination $app
$credits=Join-Path $workspace 'CREDITOS.txt'
if(!(Test-Path -LiteralPath $credits)){$credits=Join-Path $workspace 'avatar-release/CREDITOS.txt'}
Copy-Item -LiteralPath $credits -Destination $app
$licenses=Join-Path $app 'licencias';New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach($file in @('avatar-app/assets/dialogue/FFmpeg-LICENSE.txt','avatar-app/assets/dialogue/FFmpeg-README.txt','avatar-app/assets/dialogue/ffmpeg.json','avatar-app/assets/transcription/LICENSE-whisper.txt','avatar-app/assets/transcription/LICENSE-model.txt','avatar-app/assets/transcription/TRANSCRIPCION.txt','avatar-app/assets/transcription/runtime.json','avatar-app/assets/Velopack-LICENSE.txt')) {Copy-Item -LiteralPath $file -Destination $licenses}
if(!$SkipTests){
    $qa=Join-Path $workspace ('artifacts/qa-'+$Version)
    $env:AVATAR_QA_DIR=Join-Path $qa 'avatars';$env:DIALOGUE_QA_DIR=Join-Path $qa 'dialogue';$env:UPDATE_QA_DIR=Join-Path $qa 'updates'
    foreach($test in @('--self-test','--dialogue-self-test','--update-self-test')){
        $check=Start-Process -FilePath $exe -ArgumentList $test -Wait -PassThru -WindowStyle Hidden
        if($check.ExitCode -ne 0){throw ('Falló la verificación '+$test+'; consulta los archivos *selftest-error.txt.')}
    }
    Write-Host 'Personajes, diálogo, exportación y actualizaciones: pruebas correctas.'
}
$tool=Join-Path $workspace '.tools/vpk.exe'
if(!(Test-Path -LiteralPath $tool)){
    dotnet tool install vpk --tool-path (Join-Path $workspace '.tools') --version 1.2.161
    if($LASTEXITCODE -ne 0){throw 'No se pudo preparar Velopack.'}
}
$pack=@('pack','--runtime','win-x64','--packId','AxxBelmont.AvatarStudio','--packTitle','Axx Belmont Avatar Studio','--packAuthors','Axx Belmont','--packVersion',$Version,'--packDir',$app,'--mainExe','Axx-Belmont-Avatar-Studio.exe','--outputDir',$release,'--channel','win','--icon',(Join-Path $workspace 'avatar-app/assets/app.ico'),'--noPortable','--delta','None','--releaseNotes',(Join-Path $workspace ('releases/v'+$Version+'.md')))
$splash=Join-Path $workspace 'avatar-app/assets/installer-splash.png'
if(Test-Path -LiteralPath $splash){$pack+=@('--splashImage',$splash,'--splashProgressColor','#E6B94A')}
& $tool @pack
if($LASTEXITCODE -ne 0){throw 'No se pudo crear el instalador.'}
$setup=Join-Path $release 'AxxBelmont.AvatarStudio-win-Setup.exe'
if(!(Test-Path -LiteralPath $setup)){throw 'No se generó Setup.exe.'}
Copy-Item -LiteralPath $setup -Destination (Join-Path $release 'Instalar-Axx-Belmont-Avatar-Studio.exe')
Copy-Item -LiteralPath avatar-app/LEEME.txt -Destination $release
Get-ChildItem -LiteralPath $release -File | Where-Object Extension -In '.exe','.nupkg','.json' | ForEach-Object { $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; $hash.Hash.ToLowerInvariant()+'  '+$_.Name } | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding utf8
Write-Host ('Instalador y paquetes listos en '+$release)
