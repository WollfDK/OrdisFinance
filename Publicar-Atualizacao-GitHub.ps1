param(
    [string]$NovaVersao = "2.7.1",
    [string]$NotasDaVersao = "Melhorias taticas e sincronizacao do Mini-Ordis"
)

$ErrorActionPreference = "Stop"
cd "B:\PROGRAMAS\PROGRAMA FINANCEIRO PESSOAL\OrdisFinance"

Write-Host "`n=========================================================" -ForegroundColor Cyan
Write-Host "   CEPHALON ORDIS // PUBLICADOR DE ATUALIZACAO ONLINE" -ForegroundColor Green
Write-Host "=========================================================`n" -ForegroundColor Cyan

# 1. Atualiza o numero da versao no codigo C# e XAML
(Get-Content "OrdisFinance.Infrastructure\OrdisUpdateService.cs" -Raw) `
    -replace 'public const string VersaoAtualSistema = "[\d\.]+";', "public const string VersaoAtualSistema = `"$NovaVersao`";" |
    Set-Content "OrdisFinance.Infrastructure\OrdisUpdateService.cs" -Encoding UTF8

# 2. Compila o pacote de atualizacao limpo (sem dados pessoais)
$pastaRelease = "B:\PROGRAMAS\PROGRAMA FINANCEIRO PESSOAL\OrdisFinance\DIST_INSTALADOR\Pacote_Update"
if (Test-Path $pastaRelease) { Remove-Item $pastaRelease -Recurse -Force }
New-Item -ItemType Directory -Path $pastaRelease -Force | Out-Null

dotnet publish "OrdisFinance.UI\OrdisFinance.UI.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o $pastaRelease
if (Test-Path "$pastaRelease\CofreDados") { Remove-Item "$pastaRelease\CofreDados" -Recurse -Force }

$zipRelease = "B:\PROGRAMAS\PROGRAMA FINANCEIRO PESSOAL\OrdisFinance\DIST_INSTALADOR\OrdisFinance_Update_v$NovaVersao.zip"
if (Test-Path $zipRelease) { Remove-Item $zipRelease -Force }
Compress-Archive -Path "$pastaRelease\*" -DestinationPath $zipRelease -Force

# 3. Faz o Commit e Push no Git
git add .
git commit -m "Release v$NovaVersao - $NotasDaVersao"
git push -u origin main

# 4. Se tiver o GitHub CLI (gh) autenticado, ja cria a Release com o .zip anexado automaticamente!
if (Get-Command gh -ErrorAction SilentlyContinue) {
    gh release create "v$NovaVersao" $zipRelease --title "Ordis Financeiro v$NovaVersao" --notes "$NotasDaVersao"
    Write-Host "`n✔ Release v$NovaVersao publicada no GitHub com sucesso! Qualquer maquina ja pode clicar em 'ATUALIZAR'!" -ForegroundColor Green
} else {
    Write-Host "`n✔ Codigo enviado ao Git e pacote gerado em: $zipRelease" -ForegroundColor Green
    Write-Host "Anexe esse arquivo .zip na aba 'Releases' do seu repositorio no GitHub com a tag v$NovaVersao!" -ForegroundColor Yellow
}
