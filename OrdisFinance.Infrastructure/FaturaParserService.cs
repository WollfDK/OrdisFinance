using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using OrdisFinance.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace OrdisFinance.Infrastructure;

public class FaturaParserService
{
    private readonly CofreSqliteService _cofre;

    private static readonly string[] GatilhosSecaoCompras = {
        "despesas da fatura", "transações", "transacoes", "lançamentos: compras",
        "lancamentos: compras", "detalhamento da fatura", "compras nacionais",
        "movimentações na fatura", "historico de despesas", "histórico de despesas"
    };

    private static readonly string[] GatilhosSecaoIgnorar = {
        "próxima fatura", "proxima fatura", "encargos financeiros",
        "opções de parcelamento", "opcoes de parcelamento", "simulação", "simulacao"
    };

    public FaturaParserService(CofreSqliteService cofre)
    {
        _cofre = cofre;
    }

    public bool EhContaConsumoUtilidade(string textoBruto, string caminhoArquivo)
    {
        string t = (textoBruto ?? "").ToLowerInvariant();
        string nome = Path.GetFileName(caminhoArquivo ?? "").ToLowerInvariant();

        return t.Contains("santa maria") || t.Contains("kwh") || t.Contains("energia eletrica") ||
               t.Contains("energia elétrica") || t.Contains("unidade consumidora") || t.Contains("sanear") ||
               t.Contains("cesan") || t.Contains("bandeira tarifaria") || t.Contains("bandeira tarifária") ||
               nome.Contains("luz") || nome.Contains("energia") || nome.Contains("agua") || nome.Contains("internet");
    }

    public bool EhFaturaCartao(string textoBruto, string caminhoArquivo)
    {
        // Se for conta de luz/agua/internet, NUNCA trata como fatura de cartao de credito!
        if (EhContaConsumoUtilidade(textoBruto, caminhoArquivo))
            return false;

        string t = (textoBruto ?? "").ToLowerInvariant();
        string nome = Path.GetFileName(caminhoArquivo ?? "").ToLowerInvariant();

        bool temTermosCartao = GatilhosSecaoCompras.Any(g => t.Contains(g)) ||
                               t.Contains("total da sua fatura") ||
                               t.Contains("limite de crédito") ||
                               t.Contains("pagamento mínimo") ||
                               (nome.Contains("fatura") && (t.Contains("cartão") || t.Contains("cartao") || nome.Contains("inter") || nome.Contains("nubank")));

        return temTermosCartao;
    }

    public bool EhComprovantePagamentoFaturaDuplicado(string textoBruto)
    {
        string t = (textoBruto ?? "").ToLowerInvariant();
        bool ehPixOuBoleto = t.Contains("comprovante") || t.Contains("pix") || t.Contains("pagamento realizado");
        bool mencionaFatura = t.Contains("fatura") || t.Contains("cartao") || t.Contains("cartão") || t.Contains("nubank") || t.Contains("inter");
        return ehPixOuBoleto && mencionaFatura && !GatilhosSecaoCompras.Any(g => t.Contains(g)) && !t.Contains("kwh");
    }

    public decimal ExtrairValorTotalFatura(string textoBruto, string caminhoArquivo = "")
    {
        // 1. Tenta leitura geometrica da Pagina 1 se for um PDF valido de Fatura
        if (!string.IsNullOrWhiteSpace(caminhoArquivo) && File.Exists(caminhoArquivo) && Path.GetExtension(caminhoArquivo).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[]? pdfBytes = ObterBytesPdfValido(caminhoArquivo);
                if (pdfBytes != null)
                {
                    using var doc = PdfDocument.Open(pdfBytes, new ParsingOptions { UseLenientParsing = true });
                    if (doc.NumberOfPages >= 1)
                    {
                        var page1 = doc.GetPage(1);
                        var palavrasEsquerda = page1.GetWords()
                            .Where(w => w.BoundingBox.Left < 310)
                            .OrderByDescending(w => w.BoundingBox.Bottom)
                            .ThenBy(w => w.BoundingBox.Left)
                            .Select(w => w.Text);

                        string textoColunaEsquerda = string.Join(" ", palavrasEsquerda);
                        var mEsq = Regex.Match(textoColunaEsquerda, @"(?:Total\s+da\s+sua\s+fatura|fatura\s+no\s+valor\s+de|Total\s+a\s+pagar)\s+R\$\s*([\d\.]+,\d{2})", RegexOptions.IgnoreCase);
                        if (mEsq.Success && TentarConverterDecimal(mEsq.Groups[1].Value, out decimal valEsq) && valEsq > 0)
                            return valEsq;
                    }
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(textoBruto)) return 0m;

        // 2. Ancoras Universais (Contas de Luz, Agua, Boletos e Faturas)
        string[] padroesForteConfianca = {
            @"TOTAL\s+A\s+PAGAR\s*:?\s*(?:R\$\s*)?([\d\.]+,\d{2})",
            @"VALOR\s+A\s+PAGAR\s*:?\s*(?:R\$\s*)?([\d\.]+,\d{2})",
            @"VALOR\s+TOTAL\s*:?\s*R\$\s*([\d\.]+,\d{2})",
            @"FATURA\s+ATUAL\s*:?\s*R\$\s*([\d\.]+,\d{2})",
            @"VALOR\s+DO\s+DOCUMENTO\s*:?\s*(?:R\$\s*)?([\d\.]+,\d{2})",
            @"VALOR\s+COBRADO\s*:?\s*(?:R\$\s*)?([\d\.]+,\d{2})",
            @"VALOR\s+PAGO\s*:?\s*(?:R\$\s*)?([\d\.]+,\d{2})",
            @"Total\s+da\s+sua\s+fatura\s*R\$\s*([\d\.]+,\d{2})"
        };

        foreach (var padrao in padroesForteConfianca)
        {
            var matches = Regex.Matches(textoBruto, padrao, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                int inicioContexto = Math.Max(0, m.Index - 45);
                string contexto = textoBruto.Substring(inicioContexto, Math.Min(textoBruto.Length - inicioContexto, m.Length + 45)).ToLowerInvariant();
                if (contexto.Contains("rotativo") || contexto.Contains("financiado") || contexto.Contains("limite") || contexto.Contains("mínimo") || contexto.Contains("minimo"))
                    continue;

                if (TentarConverterDecimal(m.Groups[1].Value, out decimal val) && val > 0)
                    return val;
            }
        }

        // 3. Fallback: Qualquer "R$ XXX,XX" isolado no documento (muito comum em contas de luz compactas)
        var todosValores = Regex.Matches(textoBruto, @"R\$\s*([\d\.]+,\d{2})", RegexOptions.IgnoreCase);
        foreach (Match m in todosValores)
        {
            if (TentarConverterDecimal(m.Groups[1].Value, out decimal v) && v > 5m && v < 25000m)
                return v;
        }

        return 0m;
    }

    public DateTime ExtrairDataVencimento(string textoBruto)
    {
        if (string.IsNullOrWhiteSpace(textoBruto)) return DateTime.Now;

        var matchExplicito = Regex.Match(textoBruto, @"Vencimento\s*:?\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
        if (matchExplicito.Success && DateTime.TryParseExact(matchExplicito.Groups[1].Value, "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out var dt1))
            return dt1;

        var matchGeral = Regex.Match(textoBruto, @"(\d{2}/\d{2}/\d{4})");
        if (matchGeral.Success && DateTime.TryParseExact(matchGeral.Groups[1].Value, "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out var dt2))
            return dt2;

        return DateTime.Now;
    }

    public ResultadoExtracaoFatura ExtrairFaturaCompleta(string caminhoArquivo, string textoFallback)
    {
        var resultado = new ResultadoExtracaoFatura
        {
            ValorTotalFatura = ExtrairValorTotalFatura(textoFallback, caminhoArquivo),
            DataVencimento = ExtrairDataVencimento(textoFallback)
        };

        var linhasParaAnalisar = new List<string>();

        if (!string.IsNullOrWhiteSpace(caminhoArquivo) && File.Exists(caminhoArquivo) && Path.GetExtension(caminhoArquivo).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[]? pdfBytes = ObterBytesPdfValido(caminhoArquivo);
                if (pdfBytes != null)
                {
                    using var document = PdfDocument.Open(pdfBytes, new ParsingOptions { UseLenientParsing = true });
                    foreach (var page in document.GetPages())
                    {
                        string rawPageText = (page.Text ?? string.Empty).ToLowerInvariant();
                        bool temSecaoCompras = GatilhosSecaoCompras.Any(g => rawPageText.Contains(g));
                        if (!temSecaoCompras) continue;

                        var linhasPagina = AgruparPalavrasEmLinhas(page.GetWords().ToList());
                        linhasParaAnalisar.AddRange(linhasPagina);
                    }
                }
            }
            catch { }
        }

        if (!linhasParaAnalisar.Any() && !string.IsNullOrWhiteSpace(textoFallback))
        {
            var todasLinhas = textoFallback.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool dentroDespesas = false;
            foreach (var l in todasLinhas)
            {
                string lower = l.ToLowerInvariant();
                if (GatilhosSecaoCompras.Any(g => lower.Contains(g)))
                    dentroDespesas = true;
                else if (GatilhosSecaoIgnorar.Any(g => lower.Contains(g)))
                    dentroDespesas = false;

                if (dentroDespesas)
                    linhasParaAnalisar.Add(l);
            }
        }

        var regras = _cofre.ObterRegrasAprendidas();
        string cartaoAtual = "Cartão Principal";

        foreach (var linhaCrua in linhasParaAnalisar)
        {
            string linha = linhaCrua.Trim();
            if (linha.Length < 6) continue;

            if (linha.StartsWith("Total CART", StringComparison.OrdinalIgnoreCase) || linha.StartsWith("Subtotal", StringComparison.OrdinalIgnoreCase))
                continue;

            var matchCartao = Regex.Match(linha, @"CART[ÃA]O\s+(?:\d{4}\*+|\*+\s*)(\d{4})", RegexOptions.IgnoreCase);
            if (matchCartao.Success)
            {
                cartaoAtual = $"Final {matchCartao.Groups[1].Value}";
                continue;
            }

            linha = Regex.Replace(linha, @"FABIO SOUZA DOS SANTOS.*$", "", RegexOptions.IgnoreCase).Trim();
            linha = Regex.Replace(linha, @"VALOR TOTAL\s+R\$\s*[\d\.]+,\d{2}.*$", "", RegexOptions.IgnoreCase).Trim();

            var matchLancamento = Regex.Match(
                linha,
                @"^(?<data>\d{1,2}[\.\s]*de\s+[a-zA-ZçÇ]{3,4}\.?\s+\d{4}|\d{2}/\d{2}(?:/\d{2,4})?|\d{2}\s+[A-ZÇ]{3})\s+(?<desc>.+?)\s+(?<sinal>\+\s*)?(?:R\$\s*)?(?<valor>[\d\.]+,\d{2})\b",
                RegexOptions.IgnoreCase);

            if (!matchLancamento.Success)
                continue;

            string dataCompra = matchLancamento.Groups["data"].Value.Trim();
            string descBruta = matchLancamento.Groups["desc"].Value.Trim(' ', '-', '•', '|');
            bool ehCreditoOuPagamento = matchLancamento.Groups["sinal"].Success || descBruta.EndsWith("+");
            string valorStr = matchLancamento.Groups["valor"].Value;

            if (!TentarConverterDecimal(valorStr, out decimal valorItem) || valorItem <= 0)
                continue;

            descBruta = Regex.Replace(descBruta, @"\s+-\s*$", "").Trim();

            if (ehCreditoOuPagamento)
            {
                string upperDesc = descBruta.ToUpperInvariant();
                if (!upperDesc.Contains("PAGAMENTO") && !upperDesc.Contains("DEB AUT"))
                {
                    resultado.TotalCreditosAbatidos += valorItem;
                }
                continue;
            }

            string parcela = "À vista";
            var matchParc = Regex.Match(descBruta, @"\(?\s*Parc(?:ela)?\s*(\d{1,2})\s*(?:de|/)\s*(\d{1,2})\s*\)?", RegexOptions.IgnoreCase);
            if (!matchParc.Success)
                matchParc = Regex.Match(descBruta, @"\b(\d{2})/(\d{2})\b");

            if (matchParc.Success)
            {
                parcela = $"{matchParc.Groups[1].Value.PadLeft(2, '0')}/{matchParc.Groups[2].Value.PadLeft(2, '0')}";
                descBruta = descBruta.Replace(matchParc.Value, "").Trim(' ', '-');
            }

            var (categoria, conhecido) = ClassificarEstabelecimento(descBruta, regras);

            resultado.Itens.Add(new ItemFaturaExtraido
            {
                CartaoFinal = cartaoAtual,
                DataCompra = dataCompra,
                Estabelecimento = descBruta,
                Valor = valorItem,
                ParcelaInfo = parcela,
                CategoriaSugerida = categoria,
                ConhecidoPeloOrdis = conhecido
            });
        }

        if (resultado.Itens.Any())
        {
            decimal somaLiquidaReal = resultado.Itens.Sum(x => x.Valor) - resultado.TotalCreditosAbatidos;
            if (resultado.ValorTotalFatura <= 0 || Math.Abs(resultado.ValorTotalFatura - somaLiquidaReal) > (somaLiquidaReal * 0.4m))
            {
                resultado.ValorTotalFatura = somaLiquidaReal;
            }
        }

        return resultado;
    }

    public List<ItemFaturaExtraido> ExtrairItensDaFatura(string caminhoArquivo, string textoFallback)
    {
        return ExtrairFaturaCompleta(caminhoArquivo, textoFallback).Itens;
    }

    private (string Categoria, bool Conhecido) ClassificarEstabelecimento(string estabelecimento, List<RegraAprendida> regras)
    {
        string upper = estabelecimento.ToUpperInvariant();

        foreach (var r in regras.OrderByDescending(x => x.PadraoEstabelecimento.Length))
        {
            if (upper.Contains(r.PadraoEstabelecimento.ToUpperInvariant()))
                return (r.Categoria, true);
        }

        if (upper.Contains("WELLNESS") || upper.Contains("ACADEMIA") || upper.Contains("DROGARIA") || upper.Contains("FARMACIA") || upper.Contains("UNIMED"))
            return ("Saúde", true);
        if (upper.Contains("INTERCEL") || upper.Contains("UNIASSELVI") || upper.Contains("SANTA MARIA") || upper.Contains("ROTATIVO") || upper.Contains("IOF") || upper.Contains("JOF"))
            return ("Fixos", true);
        if (upper.Contains("AMAZON") || upper.Contains("HEARTOPIA") || upper.Contains("GOOGLE") || upper.Contains("CRUNCHYRO") || upper.Contains("VECTORIZER"))
            return ("Assinaturas", true);
        if (upper.Contains("HORTIFRUTI") || upper.Contains("SUPERMERCADO") || upper.Contains("LOCATELLI") || upper.Contains("CASA GRANDE") || upper.Contains("DOIS AMIGOS"))
            return ("Alimentação", true);
        if (upper.Contains("SHELL") || upper.Contains("ABAST") || upper.Contains("POSTO") || upper.Contains("COMBUSTIVEL") || upper.Contains("ROUTE 66"))
            return ("Mobilidade e Manutencao", true);
        if (upper.Contains("STEAM") || upper.Contains("LOTERIAS") || upper.Contains("ELZA LANCHES") || upper.Contains("PASTELARIA") || upper.Contains("DONA VAL") || upper.Contains("COMERCIO DE LANCHE"))
            return ("Triviais", true);

        return ("Triviais", false);
    }

    public static List<string> AgruparPalavrasEmLinhas(List<Word> words)
    {
        var resultado = new List<string>();
        if (!words.Any()) return resultado;

        var ordenadasY = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
        var linhaAtual = new List<Word>();
        double yReferencia = ordenadasY[0].BoundingBox.Bottom;

        foreach (var w in ordenadasY)
        {
            if (Math.Abs(w.BoundingBox.Bottom - yReferencia) <= 3.2)
            {
                linhaAtual.Add(w);
            }
            else
            {
                resultado.Add(string.Join(" ", linhaAtual.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text)));
                linhaAtual = new List<Word> { w };
                yReferencia = w.BoundingBox.Bottom;
            }
        }

        if (linhaAtual.Any())
            resultado.Add(string.Join(" ", linhaAtual.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text)));

        return resultado;
    }

    // INSPECIONA OS BYTES BRUTOS PARA CORTAR LIXO ANTES DE "%PDF-" OU LER FALSOS PDFs (HTML/TEXTO/IMAGEM)
    public static byte[]? ObterBytesPdfValido(string caminhoPdf)
    {
        if (!File.Exists(caminhoPdf)) return null;
        byte[] bytes = File.ReadAllBytes(caminhoPdf);
        if (bytes.Length < 5) return null;

        // Procura a sequencia "%PDF-" (0x25, 0x50, 0x44, 0x46, 0x2D) nos primeiros 8 KB do arquivo
        int limiteBusca = Math.Min(bytes.Length - 5, 8192);
        for (int i = 0; i <= limiteBusca; i++)
        {
            if (bytes[i] == 0x25 && bytes[i + 1] == 0x50 && bytes[i + 2] == 0x44 && bytes[i + 3] == 0x46 && bytes[i + 4] == 0x2D)
            {
                return i == 0 ? bytes : bytes.Skip(i).ToArray();
            }
        }
        return null;
    }

    public static bool EhBytesDeImagem(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        bool ehPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        bool ehJpg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        return ehPng || ehJpg;
    }

    public static string ExtrairLinhasGeometricasPdf(string caminhoPdf)
    {
        if (!File.Exists(caminhoPdf)) return string.Empty;
        byte[] rawBytes = File.ReadAllBytes(caminhoPdf);

        if (rawBytes.Length == 0)
            return "[AVISO: Este arquivo está com 0 bytes (vazio). Baixe o documento novamente.]";

        byte[]? pdfBytes = ObterBytesPdfValido(caminhoPdf);

        if (pdfBytes != null)
        {
            try
            {
                var sb = new StringBuilder();
                var opcoes = new ParsingOptions { UseLenientParsing = true };
                using var document = PdfDocument.Open(pdfBytes, opcoes);
                foreach (var page in document.GetPages())
                {
                    var words = page.GetWords().ToList();
                    if (!words.Any())
                    {
                        sb.AppendLine(page.Text);
                        continue;
                    }

                    foreach (var linha in AgruparPalavrasEmLinhas(words))
                        sb.AppendLine(linha);
                }
                string extraido = sb.ToString();
                if (!string.IsNullOrWhiteSpace(extraido))
                    return extraido;
            }
            catch { }
        }

        // Se era uma imagem (JPG/PNG) salva com final .pdf
        if (EhBytesDeImagem(rawBytes))
            return "[IMAGEM_DISFARCADA_DE_PDF]";

        // Se era um HTML/XML/Texto de portal de conta de luz salvo com extensao .pdf
        string textoAlternativo = Encoding.UTF8.GetString(rawBytes);
        textoAlternativo = Regex.Replace(textoAlternativo, @"<script[^>]*>[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        textoAlternativo = Regex.Replace(textoAlternativo, @"<style[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        textoAlternativo = Regex.Replace(textoAlternativo, @"<[^>]+>", " ");
        textoAlternativo = WebUtility.HtmlDecode(textoAlternativo);
        textoAlternativo = Regex.Replace(textoAlternativo, @"[^\S\r\n]{2,}", " ").Trim();

        return string.IsNullOrWhiteSpace(textoAlternativo)
            ? $"[Documento importado: {Path.GetFileName(caminhoPdf)} - Preencha o valor manualmente nas barras acima]"
            : textoAlternativo;
    }

    private static bool TentarConverterDecimal(string valorStr, out decimal resultado)
    {
        return decimal.TryParse(
            (valorStr ?? "0").Replace(".", "").Replace(",", "."),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out resultado);
    }
}
