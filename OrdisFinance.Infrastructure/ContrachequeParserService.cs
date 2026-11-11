using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using OrdisFinance.Domain;
using Tesseract;
using UglyToad.PdfPig;

namespace OrdisFinance.Infrastructure;

public class ContrachequeParserService
{
    private readonly CofreSqliteService _cofre;
    private readonly string _pastaTessData;

    public ContrachequeParserService(CofreSqliteService cofre)
    {
        _cofre = cofre;
        _pastaTessData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    }

    public (DadosContrachequeExtraido Dados, DateTime DataDetectada) AnalisarArquivoCompleto(string caminhoArquivo)
    {
        var dados = new DadosContrachequeExtraido
        {
            DescricaoSugerida = $"Contracheque ({Path.GetFileNameWithoutExtension(caminhoArquivo)})"
        };

        DateTime dataDetectada = ExtrairDataDoNomeArquivo(caminhoArquivo) ?? DateTime.Now;

        if (!File.Exists(caminhoArquivo))
            return (dados, dataDetectada);

        string ext = Path.GetExtension(caminhoArquivo).ToLowerInvariant();
        string texto = ext switch
        {
            ".pdf" => ExtrairTextoOuOcrDePdf(caminhoArquivo),
            ".png" or ".jpg" or ".jpeg" => ExecutarOcrEmBytesImagem(File.ReadAllBytes(caminhoArquivo)),
            _ => File.ReadAllText(caminhoArquivo)
        };

        if (string.IsNullOrWhiteSpace(texto))
            return (dados, dataDetectada);

        // 1. Extrai Competencia (priorizando "Outubro de 2026" ou nome do arquivo e IGNORANDO "Admissão: 29/06/2023")
        DateTime? dataCompetencia = ExtrairDataOuCompetenciaDoTexto(texto, caminhoArquivo);
        if (dataCompetencia.HasValue)
            dataDetectada = dataCompetencia.Value;

        // 2. Extrai Nome do Funcionario e ja cruza com os Perfis Salvos no Cofre para preencher o CPF mesmo quando o Holerite nao imprime CPF!
        var perfis = _cofre.ObterPerfisContracheque();
        dados.NomeTitular = ExtrairNomeTitularDoTexto(texto, perfis);

        // 3. Tenta extrair CPF do texto (ignorando CNPJ 06.098.674/0001-57)
        dados.CpfTitular = LocalizarCpfValidoNoTexto(texto);

        // 4. Se o holerite nao tem CPF impresso (como o da Blizu), puxa o CPF salvo daquele Nome no banco!
        if (string.IsNullOrWhiteSpace(dados.CpfTitular) && !string.IsNullOrWhiteSpace(dados.NomeTitular))
        {
            var perfilPorNome = perfis.FirstOrDefault(p =>
                p.NomeTitular.Equals(dados.NomeTitular, StringComparison.OrdinalIgnoreCase) ||
                p.NomeTitular.Contains(dados.NomeTitular, StringComparison.OrdinalIgnoreCase) ||
                dados.NomeTitular.Contains(p.NomeTitular, StringComparison.OrdinalIgnoreCase));

            if (perfilPorNome != null && !perfilPorNome.CpfTitular.StartsWith("SEM-CPF"))
            {
                dados.CpfTitular = perfilPorNome.CpfTitular;
            }
        }

        // 5. Extrai valores tolerando falhas de OCR ("Venomentos", "Descontus", "Valor Liquido" ou linhas separadas)
        ExtrairValoresHolerite(texto, dados);

        return (dados, dataDetectada);
    }

    private string ExtrairTextoOuOcrDePdf(string caminhoPdf)
    {
        var sb = new StringBuilder();
        try
        {
            byte[]? pdfBytes = FaturaParserService.ObterBytesPdfValido(caminhoPdf);
            if (pdfBytes != null)
            {
                using var doc = PdfDocument.Open(pdfBytes, new ParsingOptions { UseLenientParsing = true });
                foreach (var page in doc.GetPages())
                {
                    var words = page.GetWords().ToList();
                    if (words.Count > 5)
                    {
                        foreach (var linha in FaturaParserService.AgruparPalavrasEmLinhas(words))
                            sb.AppendLine(linha);
                    }
                    else
                    {
                        // PDF ESCANEADO DETECTADO (como o Holerite da Blizu)! Extrai as imagens embutidas na pagina e roda OCR!
                        foreach (var img in page.GetImages())
                        {
                            byte[]? imgBytes = null;
                            if (img.TryGetPng(out var png) && png != null && png.Length > 0)
                                imgBytes = png;
                            else if (img.RawBytes.Length > 0)
                                imgBytes = img.RawBytes.ToArray();

                            if (imgBytes != null)
                            {
                                string textoOcr = ExecutarOcrEmBytesImagem(imgBytes);
                                if (!string.IsNullOrWhiteSpace(textoOcr))
                                    sb.AppendLine(textoOcr);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return sb.ToString();
    }

    private string ExecutarOcrEmBytesImagem(byte[] imageBytes)
    {
        try
        {
            GarantirDicionarioOcrPortugues();
            string arqPor = Path.Combine(_pastaTessData, "por.traineddata");
            if (!File.Exists(arqPor)) return string.Empty;

            using var engine = new TesseractEngine(_pastaTessData, "por", EngineMode.Default);
            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(img);
            return page.GetText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void GarantirDicionarioOcrPortugues()
    {
        try
        {
            if (!Directory.Exists(_pastaTessData))
                Directory.CreateDirectory(_pastaTessData);

            string arqPor = Path.Combine(_pastaTessData, "por.traineddata");
            if (File.Exists(arqPor)) return;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var bytes = client.GetByteArrayAsync("https://github.com/tesseract-ocr/tessdata_fast/raw/main/por.traineddata").GetAwaiter().GetResult();
            if (bytes != null && bytes.Length > 100000)
                File.WriteAllBytes(arqPor, bytes);
        }
        catch { }
    }

    private void ExtrairValoresHolerite(string texto, DadosContrachequeExtraido dados)
    {
        // 1. Procura padroes tolerantes a OCR ("Venomentos", "Vencimentos", "Descontus", "Descontos", "Valor Liquido")
        var mLiq = Regex.Match(texto, @"Valor\s+L[ií]quido[\s\S]{0,60}?(\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase);
        if (mLiq.Success && TentarDecimal(mLiq.Groups[1].Value, out decimal vLiq))
            dados.ValorLiquido = vLiq;

        var mBruto = Regex.Match(texto, @"Total\s+de\s+Ven[coi\w]{4,9}tos[\s\S]{0,60}?(\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase);
        if (mBruto.Success && TentarDecimal(mBruto.Groups[1].Value, out decimal vBruto))
            dados.ValorBruto = vBruto;

        var mDesc = Regex.Match(texto, @"Total\s+de\s+Descont[ou]s[\s\S]{0,60}?(\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase);
        if (mDesc.Success && TentarDecimal(mDesc.Groups[1].Value, out decimal vDesc))
            dados.ValorDescontos = vDesc;

        // 2. PROVA REAL MATEMATICA: No holerite, Total de Vencimentos (3.571,50) - Total de Descontos (1.074,18) = Valor Liquido (2.497,32)!
        // Mesmo que o OCR embaralhe a ordem das colunas no rodape, apenas um trio de numeros satisfaz Bruto - Desconto = Liquido!
        var todosNumeros = Regex.Matches(texto, @"\b(\d{1,3}(?:\.\d{3})*,\d{2})\b")
            .Select(m => TentarDecimal(m.Groups[1].Value, out decimal d) ? d : 0m)
            .Where(d => d > 20m && d < 150000m)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        foreach (decimal candBruto in todosNumeros)
        {
            foreach (decimal candLiq in todosNumeros.Where(x => x < candBruto && x >= candBruto * 0.35m))
            {
                decimal dif = Math.Round(candBruto - candLiq, 2);
                decimal candDesc = todosNumeros.FirstOrDefault(x => Math.Abs(x - dif) <= 0.03m);
                if (candDesc > 0)
                {
                    dados.ValorBruto = candBruto;
                    dados.ValorDescontos = candDesc;
                    dados.ValorLiquido = candLiq;
                    return;
                }
            }
        }

        if (dados.ValorLiquido <= 0 && todosNumeros.Any())
            dados.ValorLiquido = todosNumeros.First();
    }

    private DateTime? ExtrairDataOuCompetenciaDoTexto(string texto, string caminhoArquivo)
    {
        // 1. Prioridade Maxima: Mes por extenso + Ano no texto ou no nome do arquivo (ex: "Outubro de 2026" ou "Holerite mensal Outubro 2026.pdf")
        string combinado = $"{Path.GetFileNameWithoutExtension(caminhoArquivo)} \n {texto}";
        var mesesMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "janeiro", 1 }, { "jan", 1 }, { "fevereiro", 2 }, { "fev", 2 }, { "março", 3 }, { "marco", 3 }, { "mar", 3 },
            { "abril", 4 }, { "abr", 4 }, { "maio", 5 }, { "mai", 5 }, { "junho", 6 }, { "jun", 6 },
            { "julho", 7 }, { "jul", 7 }, { "agosto", 8 }, { "ago", 8 }, { "setembro", 9 }, { "set", 9 },
            { "outubro", 10 }, { "out", 10 }, { "novembro", 11 }, { "nov", 11 }, { "dezembro", 12 }, { "dez", 12 }
        };

        var matchExtenso = Regex.Match(combinado, @"\b(janeiro|fevereiro|mar[çc]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s*(?:de|/|-|\s)\s*(20\d{2})\b", RegexOptions.IgnoreCase);
        if (matchExtenso.Success && mesesMap.TryGetValue(matchExtenso.Groups[1].Value.ToLowerInvariant(), out int mesExt))
        {
            int anoExt = int.Parse(matchExtenso.Groups[2].Value);
            return new DateTime(anoExt, mesExt, 5);
        }

        // 2. Datas dd/MM/yyyy que NAO estejam perto da palavra "Admissão"
        var matchesData = Regex.Matches(texto, @"\b(\d{2}/\d{2}/20\d{2})\b");
        foreach (Match m in matchesData)
        {
            int idx = Math.Max(0, m.Index - 25);
            string vizinhanca = texto.Substring(idx, Math.Min(texto.Length - idx, m.Length + 25)).ToLowerInvariant();
            if (vizinhanca.Contains("admiss")) continue;

            if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out var dt))
                return dt;
        }

        return null;
    }

    private DateTime? ExtrairDataDoNomeArquivo(string caminho)
    {
        string nome = Path.GetFileNameWithoutExtension(caminho ?? "");
        var m1 = Regex.Match(nome, @"(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])");
        if (m1.Success && DateTime.TryParseExact(m1.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
            return dt1;

        return null;
    }

    private string ExtrairNomeTitularDoTexto(string texto, List<PerfilContracheque> perfisSalvos)
    {
        foreach (var p in perfisSalvos)
        {
            if (!string.IsNullOrWhiteSpace(p.NomeTitular) && texto.Contains(p.NomeTitular, StringComparison.OrdinalIgnoreCase))
                return p.NomeTitular;
        }

        if (texto.Contains("FABIO SOUZA DOS SANTOS", StringComparison.OrdinalIgnoreCase) || texto.Contains("FABIO SOUZA", StringComparison.OrdinalIgnoreCase))
            return "Fabio Souza dos Santos";
        if (texto.Contains("THAMYRIS", StringComparison.OrdinalIgnoreCase))
            return "Thamyris Souza";

        var linhas = texto.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        for (int i = 0; i < linhas.Count; i++)
        {
            if (Regex.IsMatch(linhas[i], @"Nome\s+do\s+Funcion", RegexOptions.IgnoreCase) && i + 1 < linhas.Count)
            {
                for (int k = i + 1; k <= Math.Min(i + 3, linhas.Count - 1); k++)
                {
                    string cand = Regex.Replace(linhas[k], @"^\d+\s*", "").Trim();
                    if (cand.Length >= 6 && cand.Split(' ').Length >= 2 && !cand.Contains("CBO") && !cand.Contains("Depart"))
                        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cand.ToLowerInvariant());
                }
            }
        }
        return string.Empty;
    }

    private string LocalizarCpfValidoNoTexto(string texto)
    {
        // Ignora CNPJ (que tem /0001-)
        var matches = Regex.Matches(texto, @"(?<![\d/])(\d{3}\.?\d{3}\.?\d{3}-?\d{2})(?![\d/])");
        foreach (Match m in matches)
        {
            string digitos = LimparDigitos(m.Groups[1].Value);
            if (EhCpfMatematicamenteValido(digitos))
                return FormatarCpf(digitos);
        }
        return string.Empty;
    }

    private static bool TentarDecimal(string str, out decimal val)
    {
        return decimal.TryParse((str ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out val);
    }

    public static string LimparDigitos(string raw) => Regex.Replace(raw ?? "", @"[^\d]", "");

    public static bool EhCpfMatematicamenteValido(string cpf)
    {
        string d = LimparDigitos(cpf);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;

        int soma1 = 0;
        for (int i = 0; i < 9; i++) soma1 += (d[i] - '0') * (10 - i);
        int resto1 = (soma1 * 10) % 11;
        if (resto1 == 10) resto1 = 0;
        if (resto1 != (d[9] - '0')) return false;

        int soma2 = 0;
        for (int i = 0; i < 10; i++) soma2 += (d[i] - '0') * (11 - i);
        int resto2 = (soma2 * 10) % 11;
        if (resto2 == 10) resto2 = 0;
        return resto2 == (d[10] - '0');
    }

    public static string FormatarCpf(string raw)
    {
        string digitos = LimparDigitos(raw);
        if (digitos.Length > 11) digitos = digitos.Substring(0, 11);

        return digitos.Length switch
        {
            <= 3 => digitos,
            <= 6 => $"{digitos.Substring(0, 3)}.{digitos.Substring(3)}",
            <= 9 => $"{digitos.Substring(0, 3)}.{digitos.Substring(3, 3)}.{digitos.Substring(6)}",
            _ => $"{digitos.Substring(0, 3)}.{digitos.Substring(3, 3)}.{digitos.Substring(6, 3)}-{digitos.Substring(9)}"
        };
    }
}
