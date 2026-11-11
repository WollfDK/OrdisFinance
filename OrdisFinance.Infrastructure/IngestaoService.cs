using System.IO;
using Dapper;
using FluentResults;
using MoonSharp.Interpreter;
using OrdisFinance.Domain;
using Tesseract;

namespace OrdisFinance.Infrastructure;

public class IngestaoService
{
    private readonly CofreSqliteService _cofre;
    private readonly FaturaParserService _faturaParser;
    private readonly string _caminhoScriptLua;
    private readonly string _caminhoTessData;

    public IngestaoService(CofreSqliteService cofre, string caminhoScriptLua, string caminhoTessData = "./tessdata")
    {
        _cofre = cofre;
        _faturaParser = new FaturaParserService(cofre);
        _caminhoScriptLua = caminhoScriptLua;
        _caminhoTessData = caminhoTessData;
    }

    public Result<ItemQuarentena> ProcessarArquivoParaQuarentena(string caminhoArquivo)
    {
        try
        {
            if (!File.Exists(caminhoArquivo))
                return Result.Fail("Arquivo nao encontrado pelos sensores.");

            string extensao = Path.GetExtension(caminhoArquivo).ToLower();
            string textoBruto = extensao switch
            {
                ".pdf" => FaturaParserService.ExtrairLinhasGeometricasPdf(caminhoArquivo),
                ".txt" => File.ReadAllText(caminhoArquivo),
                ".png" or ".jpg" or ".jpeg" => ExtrairTextoImagemOcr(caminhoArquivo),
                _ => File.ReadAllText(caminhoArquivo)
            };

            // Se o arquivo .pdf na verdade continha bytes de imagem PNG/JPG, passa pelo OCR!
            if (textoBruto == "[IMAGEM_DISFARCADA_DE_PDF]")
            {
                textoBruto = ExtrairTextoImagemOcr(caminhoArquivo);
            }

            var item = ExecutarTriagemLua(caminhoArquivo, textoBruto);

            using var conn = _cofre.CriarConexaoAberta();
            var sql = @"
                INSERT INTO Quarentena 
                (CaminhoArquivo, TextoExtraidoBruto, DescricaoSugerida, ValorSugerido, DataSugerida, CategoriaSugerida, Aprovado)
                VALUES (@CaminhoArquivo, @TextoExtraidoBruto, @DescricaoSugerida, @ValorSugerido, @DataSugerida, @CategoriaSugerida, 0);
                SELECT last_insert_rowid();";

            item.Id = conn.ExecuteScalar<int>(sql, new
            {
                item.CaminhoArquivo,
                item.TextoExtraidoBruto,
                item.DescricaoSugerida,
                item.ValorSugerido,
                DataSugerida = item.DataSugerida.ToString("yyyy-MM-dd"),
                item.CategoriaSugerida
            });

            return Result.Ok(item);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Falha na ingestao do arquivo: {ex.Message}");
        }
    }

    private string ExtrairTextoImagemOcr(string caminhoImagem)
    {
        try
        {
            if (!Directory.Exists(_caminhoTessData))
                return "[Aviso OCR: Pasta tessdata ainda nao adicionada. Confirme o valor nas barras acima.]";

            using var engine = new TesseractEngine(_caminhoTessData, "por", EngineMode.Default);
            using var img = Pix.LoadFromFile(caminhoImagem);
            using var page = engine.Process(img);
            return page.GetText();
        }
        catch
        {
            return "[Imagem/PDF digitalizado importado: Confira a descrição e digite o valor nas barras acima.]";
        }
    }

    private ItemQuarentena ExecutarTriagemLua(string caminhoArquivo, string textoBruto)
    {
        var item = new ItemQuarentena
        {
            CaminhoArquivo = caminhoArquivo,
            TextoExtraidoBruto = textoBruto,
            DataSugerida = DateTime.Now,
            DescricaoSugerida = Path.GetFileNameWithoutExtension(caminhoArquivo),
            CategoriaSugerida = "Fixos",
            ValorSugerido = 0m
        };

        if (File.Exists(_caminhoScriptLua))
        {
            try
            {
                Script script = new Script();
                script.DoFile(_caminhoScriptLua);

                DynValue funcao = script.Globals.Get("analisar_comprovante");
                DynValue resposta = script.Call(funcao, textoBruto);

                if (resposta.Type == DataType.Table)
                {
                    var tabela = resposta.Table;
                    item.DescricaoSugerida = tabela.Get("descricao").CastToString() ?? item.DescricaoSugerida;
                    item.CategoriaSugerida = tabela.Get("categoria").CastToString() ?? item.CategoriaSugerida;
                    item.ValorSugerido = (decimal)(tabela.Get("valor").CastToNumber() ?? 0.0);
                }
            }
            catch { }
        }

        // 1. Se for Conta de Luz / Agua / Internet, classifica em Fixos e extrai o Total a Pagar
        if (_faturaParser.EhContaConsumoUtilidade(textoBruto, caminhoArquivo))
        {
            item.DescricaoSugerida = "Fatura Energia / Serviços Casa";
            item.CategoriaSugerida = "Fixos";
            decimal valorConta = _faturaParser.ExtrairValorTotalFatura(textoBruto, caminhoArquivo);
            if (valorConta > 0)
                item.ValorSugerido = valorConta;
        }
        // 2. Se for Fatura de Cartao de Credito
        else if (_faturaParser.EhFaturaCartao(textoBruto, caminhoArquivo))
        {
            item.DescricaoSugerida = $"Fatura Cartão ({Path.GetFileNameWithoutExtension(caminhoArquivo)})";
            item.CategoriaSugerida = "Fixos";
            decimal valorCorretoFatura = _faturaParser.ExtrairValorTotalFatura(textoBruto, caminhoArquivo);
            if (valorCorretoFatura > 0)
                item.ValorSugerido = valorCorretoFatura;
        }
        // 3. Qualquer outro comprovante cujo valor ainda esteja zerado
        else if (item.ValorSugerido <= 0)
        {
            decimal valorGeral = _faturaParser.ExtrairValorTotalFatura(textoBruto, caminhoArquivo);
            if (valorGeral > 0)
                item.ValorSugerido = valorGeral;
        }

        return item;
    }
}
