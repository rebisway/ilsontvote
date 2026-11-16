<#
IlsOntVote - publication depuis le PC (PowerShell) : compile le site et l'importeur, vérifie qu'aucun secret n'est dans la
sortie, et peut copier le tout sur la VM.

  .\deploy\publish.ps1                          publie dans publish\ (web + importer)
  .\deploy\publish.ps1 -Vm ubuntu@ma-vm         publie PUIS copie publish\, deploy\ et db\ dans /home/ubuntu/ftp/ilsontvote/
  Ensuite, sur la VM :  sudo bash /home/ubuntu/ftp/ilsontvote/deploy/update-ilsontvote.sh
#>
param(
    [string]$Vm = "",                                  # ex. ubuntu@ma-vm ; vide = pas de copie
    [string]$DossierDistant = "/home/ubuntu/ftp/ilsontvote"
)
$ErrorActionPreference = "Stop"
$racine = Split-Path -Parent $PSScriptRoot
Set-Location $racine

# Sortie propre : un fichier supprimé du code ne doit pas survivre dans une ancienne publication.
if (Test-Path publish) { Remove-Item publish -Recurse -Force }

dotnet publish src/IlsOntVote.Web/IlsOntVote.Web.csproj -c Release -o publish/web
if ($LASTEXITCODE -ne 0) { throw "La publication du site a échoué." }
dotnet publish src/IlsOntVote.Importer/IlsOntVote.Importer.csproj -c Release -o publish/importer
if ($LASTEXITCODE -ne 0) { throw "La publication de l'importeur a échoué." }

# Garde-fou : aucun fichier de configuration locale, de modèle ou de clé ne doit partir sur le serveur
# (le mot de passe de développement est dans appsettings.Development.json).
$interdits = Get-ChildItem publish -Recurse -File | Where-Object {
    $_.Name -match '^appsettings\.(Development|Local|Production)\.json$' -or
    $_.Name -like '*.example.json' -or
    $_.FullName -match '[\\/]App_Data[\\/]' -or
    $_.Name -in @('stats.key')
}
if ($interdits) {
    $interdits | ForEach-Object { Write-Host "INTERDIT dans la publication : $($_.FullName)" -ForegroundColor Red }
    throw "La publication contient des fichiers qui ne doivent pas être déployés."
}

# Identifie la version déployée (affichée par update-ilsontvote.sh).
# Un dépôt sans commit fait échouer git : ne pas l'arrêter pour ça.
$commit = $null
try { $ErrorActionPreference = "Continue"; $commit = (git rev-parse --short HEAD 2>$null) } catch { } finally { $ErrorActionPreference = "Stop" }
if (-not $commit) { $commit = "sans-commit" }
"$(Get-Date -Format 'yyyy-MM-dd HH:mm') ($commit)" | Set-Content publish/VERSION.txt -Encoding utf8

Write-Host "Publication prête : $racine\publish (web + importer)." -ForegroundColor Green

if ($Vm) {
    # On vide les anciens dossiers distants d'abord : scp -r fusionne, il laisserait de vieux fichiers.
    ssh $Vm "mkdir -p $DossierDistant && rm -rf $DossierDistant/publish $DossierDistant/deploy $DossierDistant/db"
    if ($LASTEXITCODE -ne 0) { throw "Connexion à $Vm impossible." }
    scp -r publish deploy db "${Vm}:$DossierDistant/"
    if ($LASTEXITCODE -ne 0) { throw "La copie vers $Vm a échoué." }
    Write-Host "Copié sur $Vm. Maintenant, sur la VM : sudo bash $DossierDistant/deploy/update-ilsontvote.sh" -ForegroundColor Green
} else {
    Write-Host "Copier publish\, deploy\ et db\ dans $DossierDistant/ sur la VM, puis : sudo bash $DossierDistant/deploy/update-ilsontvote.sh"
}
