using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentResults;

namespace OrdisFinance.Infrastructure;

public class ConfiguracaoUpdateGit
{
    public string RepositorioGitHub { get; set; } = "WollfDK/OrdisFinance";
}

public class InfoReleaseGitHub
{
    public string VersaoTag { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Notas { get; set; } = string.Empty;
    public string UrlDownloadZip { get; set; } = string.Empty;
    public bool TemAtualizacaoDisponivel { get; set; }
}

public class OrdisUpdateService
{
    public const string VersaoAtualSistema = "2.7.0";
    private readonly string _caminhoConfig;
    private static readonly HttpClient _http = new HttpClient();

    static OrdisUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OrdisFinance-AutoUpdater/2.7");
        _http.Timeout = TimeSpan.FromSeconds(25);
    }

    public OrdisUpdateService(string pastaBaseCofre)
    {
        if (!Directory.Exists(pastaBaseCofre))
            Directory.CreateDirectory(pastaBaseCofre);

        _caminhoConfig = Path.Combine(pastaBaseCofre, "update_config.json");
    }

    public string ObterRepositorioConfigurado()
    {
        try
        {
            if (File.Exists(_caminhoConfig))
            {
                var cfg = JsonSerializer.Deserialize<ConfiguracaoUpdateGit>(File.ReadAllText(_caminhoConfig));
                if (!string.IsNullOrWhiteSpace(cfg?.RepositorioGitHub))
                    return cfg.RepositorioGitHub.Trim();
            }
        }
        catch { }
        return "WollfDK/OrdisFinance";
    }

    public void SalvarRepositorioConfigurado(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return;
        string limpo = repo.Trim()
            .Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        var cfg = new ConfiguracaoUpdateGit { RepositorioGitHub = limpo };
        File.WriteAllText(_caminhoConfig, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public async Task<Result<InfoReleaseGitHub>> VerificarAtualizacaoOnlineAsync()
    {
        try
        {
            string repo = ObterRepositorioConfigurado();
            string urlApi = $"https://api.github.com/repos/{repo}/releases/latest";

            var resp = await _http.GetAsync(urlApi);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail($"Repositório '{repo}' ainda sem Releases públicas ou privado (Status {(int)resp.StatusCode}).");
            }

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var pTag) ? (pTag.GetString() ?? "") : "";
            string nome = root.TryGetProperty("name", out var pName) ? (pName.GetString() ?? tag) : tag;
            string body = root.TryGetProperty("body", out var pBody) ? (pBody.GetString() ?? "") : "";

            string urlZip = string.Empty;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var an) ? (an.GetString() ?? "") : "";
                    if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        urlZip = asset.TryGetProperty("browser_download_url", out var bdu) ? (bdu.GetString() ?? "") : "";
                        break;
                    }
                }
            }

            bool ehMaisNova = CompararVersoes(tag, VersaoAtualSistema) > 0;

            return Result.Ok(new InfoReleaseGitHub
            {
                VersaoTag = string.IsNullOrWhiteSpace(tag) ? VersaoAtualSistema : tag,
                Titulo = nome,
                Notas = body,
                UrlDownloadZip = urlZip,
                TemAtualizacaoDisponivel = ehMaisNova
            });
        }
        catch (Exception ex)
        {
            return Result.Fail($"Falha de conexão com o servidor de atualização: {ex.Message}");
        }
    }

    public async Task<Result> BaixarEAplicarAtualizacaoQuenteAsync(string urlZip)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(urlZip))
                return Result.Fail("Esta Release no GitHub não possui um pacote '.zip' anexado para atualização automática.");

            string pastaTemp = Path.Combine(Path.GetTempPath(), $"OrdisUpdate_{Guid.NewGuid():N}");
            Directory.CreateDirectory(pastaTemp);

            string arquivoZip = Path.Combine(pastaTemp, "update.zip");
            byte[] bytes = await _http.GetByteArrayAsync(urlZip);
            await File.WriteAllBytesAsync(arquivoZip, bytes);

            string pastaExtraida = Path.Combine(pastaTemp, "Extraido");
            ZipFile.ExtractToDirectory(arquivoZip, pastaExtraida, overwriteFiles: true);

            // Remove qualquer pasta CofreDados que porventura exista no zip para jamais sobrescrever os dados locais do Operador!
            string cofreNoZip = Path.Combine(pastaExtraida, "CofreDados");
            if (Directory.Exists(cofreNoZip))
                Directory.Delete(cofreNoZip, recursive: true);

            string pastaAppAtual = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exeAtual = Environment.ProcessPath ?? Path.Combine(pastaAppAtual, "OrdisFinance.UI.exe");
            int pidAtual = Environment.ProcessId;

            string scriptUpdater = Path.Combine(pastaTemp, "aplicar_update.ps1");
            string conteudoPs1 = $@"
Start-Sleep -Seconds 2
$proc = Get-Process -Id {pidAtual} -ErrorAction SilentlyContinue
if ($proc) {{ Wait-Process -Id {pidAtual} -Timeout 10 -ErrorAction SilentlyContinue }}
Copy-Item -Path '{pastaExtraida}\*' -Destination '{pastaAppAtual}' -Recurse -Force -Exclude 'OrdisFinance.db','identidade_cofre.json'
Start-Process -FilePath '{exeAtual}' -WorkingDirectory '{pastaAppAtual}'
";
            await File.WriteAllTextAsync(scriptUpdater, conteudoPs1, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptUpdater}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Erro ao aplicar pacote de atualização: {ex.Message}");
        }
    }

    private static int CompararVersoes(string tagRemota, string versaoLocal)
    {
        string limpaRemota = tagRemota.Trim().TrimStart('v', 'V');
        string limpaLocal = versaoLocal.Trim().TrimStart('v', 'V');

        if (Version.TryParse(limpaRemota, out var vRem) && Version.TryParse(limpaLocal, out var vLoc))
            return vRem.CompareTo(vLoc);

        return string.Compare(limpaRemota, limpaLocal, StringComparison.OrdinalIgnoreCase);
    }
}
