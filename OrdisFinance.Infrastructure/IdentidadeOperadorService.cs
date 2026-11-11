using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrdisFinance.Infrastructure;

public class DadosIdentidadeCofre
{
    public string CodigoCofre { get; set; } = string.Empty; // Formato XXX-YYYY
    public string NomeOperador { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string PalavrasChaveHash { get; set; } = string.Empty;
    public DateTime DataInstalacao { get; set; } = DateTime.Now;
}

public class IdentidadeOperadorService
{
    private readonly string _caminhoArquivo;
    public string PastaCofre => Path.GetDirectoryName(_caminhoArquivo) ?? string.Empty;
    public DadosIdentidadeCofre? IdentidadeAtual { get; private set; }

    public IdentidadeOperadorService(string pastaCofre)
    {
        if (!Directory.Exists(pastaCofre))
            Directory.CreateDirectory(pastaCofre);

        _caminhoArquivo = Path.Combine(pastaCofre, "identidade_cofre.json");
        Carregar();
    }

    public bool EstaInstalado => IdentidadeAtual != null && !string.IsNullOrWhiteSpace(IdentidadeAtual.CodigoCofre);

    public void Carregar()
    {
        try
        {
            if (File.Exists(_caminhoArquivo))
            {
                string json = File.ReadAllText(_caminhoArquivo, Encoding.UTF8);
                IdentidadeAtual = JsonSerializer.Deserialize<DadosIdentidadeCofre>(json);
            }
        }
        catch
        {
            IdentidadeAtual = null;
        }
    }

    public static string GerarMatriculaCofreAleatoria()
    {
        const string letras = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var rng = RandomNumberGenerator.Create();
        byte[] b = new byte[7];
        rng.GetBytes(b);

        char l1 = letras[b[0] % letras.Length];
        char l2 = letras[b[1] % letras.Length];
        char l3 = letras[b[2] % letras.Length];
        int num = ((b[3] << 8) | b[4]) % 10000;

        return $"{l1}{l2}{l3}-{num:D4}";
    }

    public string ObterChaveBancoInterna()
    {
        string codigo = IdentidadeAtual?.CodigoCofre ?? "ORD-0000";
        return $"OrdisVault#{codigo}#MatrixKey2077";
    }

    public void RegistrarInstalacaoInicial(string codigoCofre, string nomeOperador, string senha, string p1, string p2, string p3)
    {
        IdentidadeAtual = new DadosIdentidadeCofre
        {
            CodigoCofre = codigoCofre.Trim().ToUpperInvariant(),
            NomeOperador = nomeOperador.Trim(),
            SenhaHash = GerarHashSha256(senha.Trim()),
            PalavrasChaveHash = GerarHashPalavras(p1, p2, p3),
            DataInstalacao = DateTime.Now
        };
        SalvarArquivo();
    }

    public bool ValidarSenha(string senhaDigitada)
    {
        if (IdentidadeAtual == null) return false;
        return IdentidadeAtual.SenhaHash == GerarHashSha256((senhaDigitada ?? "").Trim());
    }

    public bool ValidarPalavrasChave(string p1, string p2, string p3)
    {
        if (IdentidadeAtual == null) return false;
        return IdentidadeAtual.PalavrasChaveHash == GerarHashPalavras(p1, p2, p3);
    }

    public void RedefinirCredenciais(string novoNomeOperador, string novaSenha)
    {
        if (IdentidadeAtual == null) return;
        if (!string.IsNullOrWhiteSpace(novoNomeOperador))
            IdentidadeAtual.NomeOperador = novoNomeOperador.Trim();
        if (!string.IsNullOrWhiteSpace(novaSenha))
            IdentidadeAtual.SenhaHash = GerarHashSha256(novaSenha.Trim());

        SalvarArquivo();
    }

    private void SalvarArquivo()
    {
        string json = JsonSerializer.Serialize(IdentidadeAtual, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_caminhoArquivo, json, Encoding.UTF8);
    }

    private static string GerarHashPalavras(string p1, string p2, string p3)
    {
        string combinadas = $"{NormalizarPalavra(p1)}|{NormalizarPalavra(p2)}|{NormalizarPalavra(p3)}";
        return GerarHashSha256(combinadas);
    }

    private static string NormalizarPalavra(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string norm = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string GerarHashSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"OrdisSalt2077::{input}"));
        return Convert.ToHexString(bytes);
    }
}

