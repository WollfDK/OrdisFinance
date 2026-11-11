using System.Data;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Dapper;
using FluentResults;
using Microsoft.Data.Sqlite;
using OrdisFinance.Domain;
using SQLitePCL;

namespace OrdisFinance.Infrastructure;

public class ResumoRendaMensalDb
{
    public string AnoMes { get; set; } = string.Empty;
    public decimal TotalMes { get; set; }
}

public class CofreSqliteService
{
    private readonly string _caminhoBanco;
    private readonly string _senhaMestre;
    private static bool _motorInicializado = false;

    public CofreSqliteService(string caminhoBanco, string senhaMestre)
    {
        _caminhoBanco = caminhoBanco;
        _senhaMestre = senhaMestre;

        if (!_motorInicializado)
        {
            Batteries_V2.Init();
            _motorInicializado = true;
        }
    }

    public IDbConnection CriarConexaoAberta()
    {
        var connString = new SqliteConnectionStringBuilder
        {
            DataSource = _caminhoBanco,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = _senhaMestre
        }.ToString();

        var conexao = new SqliteConnection(connString);
        conexao.Open();
        return conexao;
    }

    public Result InicializarCofre()
    {
        try
        {
            var pasta = Path.GetDirectoryName(_caminhoBanco);
            if (!string.IsNullOrEmpty(pasta) && !Directory.Exists(pasta))
                Directory.CreateDirectory(pasta);

            using var conn = CriarConexaoAberta();
            
            var sql = @"
                CREATE TABLE IF NOT EXISTS Transacoes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Descricao TEXT NOT NULL,
                    Valor REAL NOT NULL,
                    ValorBruto REAL DEFAULT 0,
                    ValorDescontos REAL DEFAULT 0,
                    Data TEXT NOT NULL,
                    Tipo INTEGER NOT NULL,
                    Categoria TEXT NOT NULL,
                    FormaPagamento TEXT DEFAULT 'Pix',
                    ParcelaInfo TEXT DEFAULT '',
                    OrigemArquivo TEXT NOT NULL,
                    TitularNome TEXT DEFAULT '',
                    TitularCpf TEXT DEFAULT '',
                    NaturezaReceita TEXT DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS PerfisContracheque (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NomeTitular TEXT NOT NULL,
                    CpfTitular TEXT UNIQUE NOT NULL,
                    ValorBrutoPadrao REAL NOT NULL,
                    ValorDescontosPadrao REAL NOT NULL,
                    ValorLiquidoPadrao REAL NOT NULL,
                    DescricaoPadrao TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Quarentena (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CaminhoArquivo TEXT NOT NULL,
                    TextoExtraidoBruto TEXT,
                    DescricaoSugerida TEXT,
                    ValorSugerido REAL,
                    DataSugerida TEXT,
                    CategoriaSugerida TEXT,
                    Aprovado INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS EventosSazonais (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Titulo TEXT NOT NULL,
                    ValorAlvo REAL NOT NULL,
                    DataVencimento TEXT NOT NULL,
                    MesesAntecedenciaAlerta INTEGER DEFAULT 4
                );

                CREATE TABLE IF NOT EXISTS CaixinhasPlanejamento (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Titulo TEXT NOT NULL,
                    ValorAlvo REAL NOT NULL,
                    ValorAtual REAL DEFAULT 0,
                    DataMeta TEXT NOT NULL,
                    Prioridade TEXT DEFAULT 'Alta'
                );

                CREATE TABLE IF NOT EXISTS Categorias (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nome TEXT UNIQUE NOT NULL,
                    Origem TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS RegrasAprendidas (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PadraoEstabelecimento TEXT UNIQUE NOT NULL,
                    Categoria TEXT NOT NULL
                );";

            conn.Execute(sql);

            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN FormaPagamento TEXT DEFAULT 'Pix';"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN ParcelaInfo TEXT DEFAULT '';"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN ValorBruto REAL DEFAULT 0;"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN ValorDescontos REAL DEFAULT 0;"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN TitularNome TEXT DEFAULT '';"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN TitularCpf TEXT DEFAULT '';"); } catch { }
            try { conn.Execute("ALTER TABLE Transacoes ADD COLUMN NaturezaReceita TEXT DEFAULT '';"); } catch { }

            conn.Execute("DELETE FROM Categorias WHERE Nome = 'Cartão' OR Nome = 'Cartao';");

            var totalCat = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Categorias");
            if (totalCat == 0)
            {
                string[] iniciais = { "Fixos", "Mobilidade e Manutencao", "Triviais", "Saúde", "Assinaturas", "Alimentação" };
                foreach (var cat in iniciais)
                {
                    conn.Execute("INSERT OR IGNORE INTO Categorias (Nome, Origem) VALUES (@Nome, 'Sistema')", new { Nome = cat });
                }
            }

            var totalRegras = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM RegrasAprendidas");
            if (totalRegras == 0)
            {
                var sementes = new Dictionary<string, string>
                {
                    { "ACADEMIA WELLNESS", "Saúde" },
                    { "WELLNESS", "Saúde" },
                    { "FARMACIA", "Saúde" },
                    { "INTERCEL", "Fixos" },
                    { "RCGA UNIC INTERCEL", "Fixos" },
                    { "SANTA MARIA", "Fixos" },
                    { "HORTIFRUTI", "Alimentação" },
                    { "SUPERMERCADO", "Alimentação" },
                    { "ATACADAO", "Alimentação" },
                    { "GOOGLE HEARTOPIA", "Assinaturas" },
                    { "HEARTOPIA", "Assinaturas" },
                    { "AMAZONPRIMEBR", "Assinaturas" },
                    { "AMAZON", "Assinaturas" },
                    { "STEAM", "Triviais" },
                    { "LOTERIAS", "Triviais" },
                    { "PLUS DELIVERY", "Triviais" },
                    { "ROUTE 66", "Mobilidade e Manutencao" },
                    { "POSTO", "Mobilidade e Manutencao" },
                    { "BOSCH", "Mobilidade e Manutencao" }
                };

                foreach (var kv in sementes)
                {
                    conn.Execute("INSERT OR IGNORE INTO RegrasAprendidas (PadraoEstabelecimento, Categoria) VALUES (@Padrao, @Cat)",
                        new { Padrao = kv.Key, Cat = kv.Value });
                }
            }

            return Result.Ok();
        }
        catch
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_caminhoBanco))
                    File.Delete(_caminhoBanco);

                using var connNovo = CriarConexaoAberta();
                return InicializarCofre();
            }
            catch (Exception exFinal)
            {
                return Result.Fail($"Falha ao recriar Cofre com a nova credencial: {exFinal.Message}");
            }
        }
    }

    public (decimal RendaReferencia, bool EhMedia3Meses, int QtdMesesMedia, string EtiquetaCaixa) ObterRendaOuMedia3Meses(string prefixoAnoMes)
    {
        using var conn = CriarConexaoAberta();

        decimal rendaNoMes = conn.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(Valor), 0) FROM Transacoes 
            WHERE Tipo = 1 AND substr(Data, 1, 7) = @Ym AND COALESCE(NaturezaReceita, '') != 'Reembolso / Divisão Pix'",
            new { Ym = prefixoAnoMes });

        if (rendaNoMes > 0)
        {
            return (rendaNoMes, false, 1, $"CAIXA REAL: R$ {rendaNoMes:N2}");
        }

        var ultimos3Meses = conn.Query<ResumoRendaMensalDb>(@"
            SELECT substr(Data, 1, 7) AS AnoMes, COALESCE(SUM(Valor), 0) AS TotalMes
            FROM Transacoes
            WHERE Tipo = 1 AND COALESCE(NaturezaReceita, '') != 'Reembolso / Divisão Pix'
              AND substr(Data, 1, 7) <= @Ym
            GROUP BY substr(Data, 1, 7)
            HAVING TotalMes > 0
            ORDER BY AnoMes DESC
            LIMIT 3;", new { Ym = prefixoAnoMes }).ToList();

        if (!ultimos3Meses.Any())
        {
            ultimos3Meses = conn.Query<ResumoRendaMensalDb>(@"
                SELECT substr(Data, 1, 7) AS AnoMes, COALESCE(SUM(Valor), 0) AS TotalMes
                FROM Transacoes
                WHERE Tipo = 1 AND COALESCE(NaturezaReceita, '') != 'Reembolso / Divisão Pix'
                GROUP BY substr(Data, 1, 7)
                HAVING TotalMes > 0
                ORDER BY AnoMes DESC
                LIMIT 3;").ToList();
        }

        if (ultimos3Meses.Any())
        {
            decimal media = Math.Round(ultimos3Meses.Average(x => x.TotalMes), 2);
            int qtd = ultimos3Meses.Count;
            return (media, true, qtd, $"MÉDIA 3 MESES ({qtd}m): R$ {media:N2}");
        }

        var perfis = ObterPerfisContracheque();
        decimal somaPerfis = perfis.Sum(p => p.ValorLiquidoPadrao);
        if (somaPerfis > 0)
            return (somaPerfis, true, 0, $"MÉDIA PREVISTA: R$ {somaPerfis:N2}");

        return (5000m, true, 0, "MÉDIA BASE: R$ 5.000,00");
    }

    public ResumoParcelamentosFuturos CalcularParcelamentosFuturos(string prefixoAnoMes)
    {
        var resumo = new ResumoParcelamentosFuturos();

        if (string.IsNullOrWhiteSpace(prefixoAnoMes) || prefixoAnoMes.Length < 7)
            prefixoAnoMes = DateTime.Now.ToString("yyyy-MM");

        int.TryParse(prefixoAnoMes.Substring(0, 4), out int anoRef);
        int.TryParse(prefixoAnoMes.Substring(5, 2), out int mesRef);
        if (anoRef < 2000) anoRef = DateTime.Now.Year;
        if (mesRef < 1 || mesRef > 12) mesRef = DateTime.Now.Month;

        using var conn = CriarConexaoAberta();
        var saidas = conn.Query<Transacao>(@"
            SELECT * FROM Transacoes 
            WHERE Tipo = 2 
            ORDER BY Data DESC, Id DESC").ToList();

        var contratosProcessados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in saidas)
        {
            if (!TentarExtrairParcela(t.ParcelaInfo, t.Descricao, out int pAtual, out int pTotal))
                continue;

            string estabLimpo = LimparChaveEstabelecimento(t.Descricao);
            string chaveContrato = $"{estabLimpo}|{pTotal}|{Math.Round(t.Valor, 0)}";
            if (contratosProcessados.Contains(chaveContrato))
                continue;

            contratosProcessados.Add(chaveContrato);

            int diffMeses = ((anoRef - t.Data.Year) * 12) + (mesRef - t.Data.Month);
            int parcelaNoMesRef = pAtual + diffMeses;

            if (parcelaNoMesRef >= 1 && parcelaNoMesRef <= pTotal)
            {
                bool jaLancadaNesteMes = (diffMeses == 0);
                int parcelasPorPagar = jaLancadaNesteMes
                    ? (pTotal - parcelaNoMesRef)
                    : (pTotal - parcelaNoMesRef + 1);

                decimal saldoDevedorContrato = Math.Round(parcelasPorPagar * t.Valor, 2);

                int mesesAteQuitar = pTotal - parcelaNoMesRef;
                DateTime dataTermino = new DateTime(anoRef, mesRef, 1).AddMonths(Math.Max(0, mesesAteQuitar));

                if (jaLancadaNesteMes)
                    resumo.ValorParcelasJaLancadasNoMes += t.Valor;
                else if (diffMeses > 0)
                    resumo.ValorParcelasProjetadasNoMes += t.Valor;

                resumo.SaldoTotalRestanteGeral += saldoDevedorContrato;

                if (parcelasPorPagar > 0 || !jaLancadaNesteMes)
                {
                    resumo.QtdContratosAtivos++;
                    resumo.ItensAtivos.Add(new ItemParcelamentoFuturo
                    {
                        Descricao = Regex.Replace(t.Descricao, @"\s*\(?Parcela\s*\d{1,2}/\d{1,2}\)?", "", RegexOptions.IgnoreCase).Trim(),
                        ValorParcela = t.Valor,
                        ParcelaAtualNaReferencia = parcelaNoMesRef,
                        TotalParcelas = pTotal,
                        ParcelasRestantesAposMes = parcelasPorPagar,
                        SaldoRestanteTotal = saldoDevedorContrato,
                        MesTermino = dataTermino.ToString("MM/yyyy"),
                        JaLancadaNoMesReferencia = jaLancadaNesteMes
                    });
                }
            }
        }

        return resumo;
    }

    public static bool TentarExtrairParcela(string? parcelaInfo, string? descricao, out int atual, out int total)
    {
        atual = 0;
        total = 0;

        string pInfo = (parcelaInfo ?? "").Trim();
        if (!string.IsNullOrEmpty(pInfo))
        {
            var m = Regex.Match(pInfo, @"(\d{1,2})\s*(?:/|de)\s*(\d{1,2})", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out atual) && int.TryParse(m.Groups[2].Value, out total))
            {
                if (atual >= 1 && total >= 2 && total <= 72 && atual <= total)
                    return true;
            }
        }

        string desc = (descricao ?? "").Trim();
        if (!string.IsNullOrEmpty(desc))
        {
            var mDesc = Regex.Match(desc, @"(?:PARC(?:ELA)?\s*|\b)(\d{1,2})/(\d{1,2})\b", RegexOptions.IgnoreCase);
            if (mDesc.Success && int.TryParse(mDesc.Groups[1].Value, out atual) && int.TryParse(mDesc.Groups[2].Value, out total))
            {
                if (atual >= 1 && total >= 2 && total <= 72 && atual <= total)
                    return true;
            }
        }

        return false;
    }

    public List<SugestaoDescricaoHistorico> ObterSugestoesDescricao()
    {
        using var conn = CriarConexaoAberta();
        var resultado = new List<SugestaoDescricaoHistorico>();

        var historico = conn.Query<SugestaoDescricaoHistorico>(@"
            SELECT Descricao, Categoria, COALESCE(NULLIF(FormaPagamento, ''), 'Pix') AS FormaPagamento, Valor AS UltimoValor
            FROM Transacoes
            WHERE Tipo = 2 AND TRIM(Descricao) != ''
            ORDER BY Id DESC").ToList();

        foreach (var h in historico)
        {
            string descLimpa = Regex.Replace(h.Descricao, @"^\[Final\s+\d{4}\]\s*", "").Trim();
            if (!string.IsNullOrWhiteSpace(descLimpa) && !resultado.Any(x => x.Descricao.Equals(descLimpa, StringComparison.OrdinalIgnoreCase)))
            {
                resultado.Add(new SugestaoDescricaoHistorico
                {
                    Descricao = descLimpa,
                    Categoria = h.Categoria,
                    FormaPagamento = h.FormaPagamento,
                    UltimoValor = h.UltimoValor
                });
            }
        }

        var regras = ObterRegrasAprendidas();
        foreach (var r in regras)
        {
            if (!resultado.Any(x => x.Descricao.Equals(r.PadraoEstabelecimento, StringComparison.OrdinalIgnoreCase)))
            {
                resultado.Add(new SugestaoDescricaoHistorico
                {
                    Descricao = r.PadraoEstabelecimento,
                    Categoria = r.Categoria,
                    FormaPagamento = "Pix",
                    UltimoValor = 0m
                });
            }
        }

        return resultado.OrderBy(x => x.Descricao).ToList();
    }

    public List<EventoSazonal> ObterEventosSazonais()
    {
        using var conn = CriarConexaoAberta();
        return conn.Query<EventoSazonal>("SELECT * FROM EventosSazonais ORDER BY DataVencimento ASC").ToList();
    }

    public void SalvarEventoSazonal(EventoSazonal ev)
    {
        using var conn = CriarConexaoAberta();
        if (ev.Id > 0)
        {
            conn.Execute(@"
                UPDATE EventosSazonais SET
                    Titulo = @Titulo,
                    ValorAlvo = @ValorAlvo,
                    DataVencimento = @DataStr,
                    MesesAntecedenciaAlerta = @MesesAntecedenciaAlerta
                WHERE Id = @Id",
                new { ev.Id, ev.Titulo, ev.ValorAlvo, DataStr = ev.DataVencimento.ToString("yyyy-MM-dd"), ev.MesesAntecedenciaAlerta });
        }
        else
        {
            conn.Execute(@"
                INSERT INTO EventosSazonais (Titulo, ValorAlvo, DataVencimento, MesesAntecedenciaAlerta)
                VALUES (@Titulo, @ValorAlvo, @DataStr, @MesesAntecedenciaAlerta)",
                new { ev.Titulo, ev.ValorAlvo, DataStr = ev.DataVencimento.ToString("yyyy-MM-dd"), ev.MesesAntecedenciaAlerta });
        }
    }

    public void ExcluirEventoSazonal(int id)
    {
        using var conn = CriarConexaoAberta();
        conn.Execute("DELETE FROM EventosSazonais WHERE Id = @Id", new { Id = id });
    }

    public List<CaixinhaPlanejamento> ObterCaixinhas()
    {
        using var conn = CriarConexaoAberta();
        return conn.Query<CaixinhaPlanejamento>("SELECT * FROM CaixinhasPlanejamento ORDER BY Id ASC").ToList();
    }

    public void SalvarCaixinha(CaixinhaPlanejamento c)
    {
        using var conn = CriarConexaoAberta();
        if (c.Id > 0)
        {
            conn.Execute(@"
                UPDATE CaixinhasPlanejamento SET
                    Titulo = @Titulo,
                    ValorAlvo = @ValorAlvo,
                    ValorAtual = @ValorAtual,
                    DataMeta = @DataStr,
                    Prioridade = @Prioridade
                WHERE Id = @Id",
                new { c.Id, c.Titulo, c.ValorAlvo, c.ValorAtual, DataStr = c.DataMeta.ToString("yyyy-MM-dd"), c.Prioridade });
        }
        else
        {
            conn.Execute(@"
                INSERT INTO CaixinhasPlanejamento (Titulo, ValorAlvo, ValorAtual, DataMeta, Prioridade)
                VALUES (@Titulo, @ValorAlvo, @ValorAtual, @DataStr, @Prioridade)",
                new { c.Titulo, c.ValorAlvo, c.ValorAtual, DataStr = c.DataMeta.ToString("yyyy-MM-dd"), c.Prioridade });
        }
    }

    public void ExcluirCaixinha(int id)
    {
        using var conn = CriarConexaoAberta();
        conn.Execute("DELETE FROM CaixinhasPlanejamento WHERE Id = @Id", new { Id = id });
    }

    public List<PerfilContracheque> ObterPerfisContracheque()
    {
        using var conn = CriarConexaoAberta();
        return conn.Query<PerfilContracheque>("SELECT * FROM PerfisContracheque ORDER BY NomeTitular COLLATE NOCASE ASC").ToList();
    }

    public void SalvarPerfilContracheque(PerfilContracheque perfil)
    {
        if (string.IsNullOrWhiteSpace(perfil.NomeTitular)) return;
        string nomeLimpo = perfil.NomeTitular.Trim();
        string cpfLimpo = (perfil.CpfTitular ?? "").Trim();

        using var conn = CriarConexaoAberta();
        var existente = conn.QueryFirstOrDefault<PerfilContracheque>(@"
            SELECT * FROM PerfisContracheque 
            WHERE LOWER(NomeTitular) = LOWER(@Nome) 
               OR (@Cpf != '' AND CpfTitular = @Cpf)",
            new { Nome = nomeLimpo, Cpf = cpfLimpo });

        if (existente != null)
        {
            conn.Execute(@"
                UPDATE PerfisContracheque SET
                    NomeTitular = @Nome,
                    CpfTitular = CASE WHEN @Cpf != '' THEN @Cpf ELSE CpfTitular END,
                    ValorBrutoPadrao = CASE WHEN @Bruto > 0 THEN @Bruto ELSE ValorBrutoPadrao END,
                    ValorDescontosPadrao = CASE WHEN @Desc >= 0 THEN @Desc ELSE ValorDescontosPadrao END,
                    ValorLiquidoPadrao = CASE WHEN @Liq > 0 THEN @Liq ELSE ValorLiquidoPadrao END,
                    DescricaoPadrao = @Descricao
                WHERE Id = @Id",
                new
                {
                    existente.Id,
                    Nome = nomeLimpo,
                    Cpf = cpfLimpo,
                    Bruto = perfil.ValorBrutoPadrao,
                    Desc = perfil.ValorDescontosPadrao,
                    Liq = perfil.ValorLiquidoPadrao,
                    Descricao = perfil.DescricaoPadrao
                });
        }
        else
        {
            string chaveCpf = string.IsNullOrWhiteSpace(cpfLimpo) ? $"SEM-CPF-{Guid.NewGuid():N}".Substring(0, 18) : cpfLimpo;
            conn.Execute(@"
                INSERT INTO PerfisContracheque (NomeTitular, CpfTitular, ValorBrutoPadrao, ValorDescontosPadrao, ValorLiquidoPadrao, DescricaoPadrao)
                VALUES (@Nome, @Cpf, @Bruto, @Desc, @Liq, @Descricao)",
                new
                {
                    Nome = nomeLimpo,
                    Cpf = chaveCpf,
                    Bruto = perfil.ValorBrutoPadrao,
                    Desc = perfil.ValorDescontosPadrao,
                    Liq = perfil.ValorLiquidoPadrao,
                    Descricao = perfil.DescricaoPadrao
                });
        }
    }

    public (decimal RendaLiquidaReal, decimal ReembolsosAbatidos, decimal GastosBrutos, decimal GastosLiquidos) CalcularBalancoMes(string prefixoAnoMes)
    {
        using var conn = CriarConexaoAberta();

        decimal rendaReal = conn.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(Valor), 0) FROM Transacoes 
            WHERE Tipo = 1 AND substr(Data, 1, 7) = @Ym AND COALESCE(NaturezaReceita, '') != 'Reembolso / Divisão Pix'",
            new { Ym = prefixoAnoMes });

        decimal reembolsos = conn.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(Valor), 0) FROM Transacoes 
            WHERE Tipo = 1 AND substr(Data, 1, 7) = @Ym AND NaturezaReceita = 'Reembolso / Divisão Pix'",
            new { Ym = prefixoAnoMes });

        decimal gastosBrutos = conn.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(Valor), 0) FROM Transacoes 
            WHERE Tipo = 2 AND substr(Data, 1, 7) = @Ym",
            new { Ym = prefixoAnoMes });

        decimal gastosLiquidos = Math.Max(0m, gastosBrutos - reembolsos);
        return (rendaReal, reembolsos, gastosBrutos, gastosLiquidos);
    }

    public List<string> ObterCategorias()
    {
        using var conn = CriarConexaoAberta();
        return conn.Query<string>("SELECT Nome FROM Categorias ORDER BY Nome COLLATE NOCASE ASC").ToList();
    }

    public void GarantirCategoria(string nomeCategoria, string origem = "Mini-Ordis")
    {
        if (string.IsNullOrWhiteSpace(nomeCategoria)) return;
        string limpa = nomeCategoria.Trim();
        if (limpa.Equals("Cartão", StringComparison.OrdinalIgnoreCase) || limpa.Equals("Cartao", StringComparison.OrdinalIgnoreCase))
            return;

        using var conn = CriarConexaoAberta();
        conn.Execute("INSERT OR IGNORE INTO Categorias (Nome, Origem) VALUES (@Nome, @Origem)", 
            new { Nome = limpa, Origem = origem });
    }

    public (int QtdTransacoes, int QtdQuarentena, int QtdRegras) ContarImpactoCategoria(string nomeCategoria)
    {
        using var conn = CriarConexaoAberta();
        int t = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Transacoes WHERE LOWER(Categoria) = LOWER(@Cat)", new { Cat = nomeCategoria });
        int q = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Quarentena WHERE Aprovado = 0 AND LOWER(CategoriaSugerida) = LOWER(@Cat)", new { Cat = nomeCategoria });
        int r = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM RegrasAprendidas WHERE LOWER(Categoria) = LOWER(@Cat)", new { Cat = nomeCategoria });
        return (t, q, r);
    }

    public Result RenomearCategoriaEmCascata(string nomeAntigo, string nomeNovo)
    {
        try
        {
            string antigo = (nomeAntigo ?? "").Trim();
            string novo = (nomeNovo ?? "").Trim();

            if (string.IsNullOrWhiteSpace(antigo) || string.IsNullOrWhiteSpace(novo))
                return Result.Fail("O nome da categoria não pode ficar vazio.");

            if (novo.Equals("Cartão", StringComparison.OrdinalIgnoreCase) || novo.Equals("Cartao", StringComparison.OrdinalIgnoreCase))
                return Result.Fail("A diretriz do Operador proíbe 'Cartão' como categoria.");

            using var conn = CriarConexaoAberta();
            using var trans = conn.BeginTransaction();

            conn.Execute("INSERT OR IGNORE INTO Categorias (Nome, Origem) VALUES (@Novo, 'Operador')", new { Novo = novo }, trans);
            if (!antigo.Equals(novo, StringComparison.OrdinalIgnoreCase))
            {
                conn.Execute("DELETE FROM Categorias WHERE LOWER(Nome) = LOWER(@Antigo)", new { Antigo = antigo }, trans);
            }
            else
            {
                conn.Execute("UPDATE Categorias SET Nome = @Novo WHERE LOWER(Nome) = LOWER(@Antigo)", new { Novo = novo, Antigo = antigo }, trans);
            }

            conn.Execute("UPDATE Transacoes SET Categoria = @Novo WHERE LOWER(Categoria) = LOWER(@Antigo)", new { Novo = novo, Antigo = antigo }, trans);
            conn.Execute("UPDATE Quarentena SET CategoriaSugerida = @Novo WHERE LOWER(CategoriaSugerida) = LOWER(@Antigo)", new { Novo = novo, Antigo = antigo }, trans);
            conn.Execute("UPDATE RegrasAprendidas SET Categoria = @Novo WHERE LOWER(Categoria) = LOWER(@Antigo)", new { Novo = novo, Antigo = antigo }, trans);

            trans.Commit();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Erro ao renomear categoria: {ex.Message}");
        }
    }

    public Result RemoverCategoriaComMigracao(string categoriaRemover, string categoriaDestino)
    {
        try
        {
            string remover = (categoriaRemover ?? "").Trim();
            string destino = (categoriaDestino ?? "").Trim();

            if (string.IsNullOrWhiteSpace(remover) || string.IsNullOrWhiteSpace(destino))
                return Result.Fail("Selecione a categoria de destino para migrar os lançamentos.");

            if (remover.Equals(destino, StringComparison.OrdinalIgnoreCase))
                return Result.Fail("A categoria de destino deve ser diferente da categoria que será removida.");

            using var conn = CriarConexaoAberta();
            using var trans = conn.BeginTransaction();

            conn.Execute("UPDATE Transacoes SET Categoria = @Destino WHERE LOWER(Categoria) = LOWER(@Remover)", new { Destino = destino, Remover = remover }, trans);
            conn.Execute("UPDATE Quarentena SET CategoriaSugerida = @Destino WHERE LOWER(CategoriaSugerida) = LOWER(@Remover)", new { Destino = destino, Remover = remover }, trans);
            conn.Execute("UPDATE RegrasAprendidas SET Categoria = @Destino WHERE LOWER(Categoria) = LOWER(@Remover)", new { Destino = destino, Remover = remover }, trans);
            conn.Execute("DELETE FROM Categorias WHERE LOWER(Nome) = LOWER(@Remover)", new { Remover = remover }, trans);

            trans.Commit();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Erro ao remover categoria: {ex.Message}");
        }
    }

    public List<RegraAprendida> ObterRegrasAprendidas()
    {
        using var conn = CriarConexaoAberta();
        return conn.Query<RegraAprendida>("SELECT * FROM RegrasAprendidas").ToList();
    }

    public void AprenderRegraEstabelecimento(string estabelecimento, string categoria)
    {
        if (string.IsNullOrWhiteSpace(estabelecimento) || string.IsNullOrWhiteSpace(categoria)) return;
        string chave = LimparChaveEstabelecimento(estabelecimento);
        if (chave.Length < 3) return;

        GarantirCategoria(categoria, "Operador");
        using var conn = CriarConexaoAberta();
        conn.Execute(@"
            INSERT INTO RegrasAprendidas (PadraoEstabelecimento, Categoria) 
            VALUES (@Padrao, @Cat)
            ON CONFLICT(PadraoEstabelecimento) DO UPDATE SET Categoria = @Cat;",
            new { Padrao = chave, Cat = categoria.Trim() });
    }

    public static string LimparChaveEstabelecimento(string raw)
    {
        string s = Regex.Replace(raw.ToUpperInvariant(), @"^\[FINAL\s+\d{4}\]\s*", "");
        s = Regex.Replace(s, @"\(?PARC(?:ELA)?\s*\d{1,2}/\d{1,2}\)?", "");
        s = Regex.Replace(s, @"\b\d{1,2}/\d{1,2}\b", "");
        s = Regex.Replace(s, @"PARC\s*\d+.*", "");
        return s.Trim(' ', '-', '*', '.');
    }

    // SUPER HOT-BACKUP: Salva o Banco SQLite Criptografado + Identidade (Login, Senha Hash, 3 Palavras-Chave e Codigo XXX-YYYY) numa Capsula .ordisbak!
    public Result RealizarBackupRapido(string pastaDestinoBackup)
    {
        try
        {
            if (!Directory.Exists(pastaDestinoBackup))
                Directory.CreateDirectory(pastaDestinoBackup);

            string pastaBaseCofre = Path.GetDirectoryName(_caminhoBanco) ?? "";
            string caminhoIdentidade = Path.Combine(pastaBaseCofre, "identidade_cofre.json");

            string carimboTempo = DateTime.Now.ToString("yyyy_MM_dd_HHmmss");
            var nomeDbTemp = $"OrdisVault_Backup_{carimboTempo}.db";
            var caminhoDbBackup = Path.Combine(pastaDestinoBackup, nomeDbTemp);

            using (var origem = (SqliteConnection)CriarConexaoAberta())
            {
                var destBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = caminhoDbBackup,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Password = _senhaMestre
                }.ToString();

                using var destino = new SqliteConnection(destBuilder);
                destino.Open();
                origem.BackupDatabase(destino);
            }
            SqliteConnection.ClearAllPools();

            // Empacota o Banco + Identidade dentro da Capsula Portatil (.ordisbak)
            string caminhoCapsula = Path.Combine(pastaDestinoBackup, $"OrdisCapsule_{carimboTempo}.ordisbak");
            using (var zip = ZipFile.Open(caminhoCapsula, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(caminhoDbBackup, "OrdisFinance.db", CompressionLevel.Optimal);
                if (File.Exists(caminhoIdentidade))
                {
                    zip.CreateEntryFromFile(caminhoIdentidade, "identidade_cofre.json", CompressionLevel.Optimal);
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Erro no Hot-Backup Completo: {ex.Message}");
        }
    }

    // RESTAURADOR DE CAPSULA DE BACKUP (.ordisbak ou .db): Importa Usuario, Senha, 3 Palavras-Chave, Codigo XXX-YYYY e todos os Lancamentos!
    public static Result<string> RestaurarCapsulaBackup(string arquivoSelecionado, string pastaCofreDestino)
    {
        try
        {
            if (!File.Exists(arquivoSelecionado))
                return Result.Fail("O arquivo de backup selecionado não foi encontrado.");

            if (!Directory.Exists(pastaCofreDestino))
                Directory.CreateDirectory(pastaCofreDestino);

            SqliteConnection.ClearAllPools();
            string ext = Path.GetExtension(arquivoSelecionado).ToLowerInvariant();
            string destinoDb = Path.Combine(pastaCofreDestino, "OrdisFinance.db");
            string destinoJson = Path.Combine(pastaCofreDestino, "identidade_cofre.json");

            if (ext == ".ordisbak" || ext == ".zip")
            {
                using var archive = ZipFile.OpenRead(arquivoSelecionado);
                var entryDb = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
                var entryJson = archive.Entries.FirstOrDefault(e => e.Name.Equals("identidade_cofre.json", StringComparison.OrdinalIgnoreCase));

                if (entryDb == null)
                    return Result.Fail("Este arquivo .ordisbak não contém um banco de dados válido do Ordis.");

                entryDb.ExtractToFile(destinoDb, overwrite: true);

                if (entryJson != null)
                {
                    entryJson.ExtractToFile(destinoJson, overwrite: true);
                    return Result.Ok("Cápsula Completa restaurada (Identidade + Login + 3 Palavras-Chave + Banco de Dados)!");
                }

                return Result.Ok("Banco de dados restaurado com sucesso!");
            }
            else if (ext == ".db")
            {
                File.Copy(arquivoSelecionado, destinoDb, overwrite: true);
                // Verifica se existe um identidade_cofre.json na mesma pasta do .db para puxar junto se houver
                string jsonVizinho = Path.Combine(Path.GetDirectoryName(arquivoSelecionado) ?? "", "identidade_cofre.json");
                if (File.Exists(jsonVizinho))
                {
                    File.Copy(jsonVizinho, destinoJson, overwrite: true);
                    return Result.Ok("Banco de dados e arquivo de Identidade vizinho restaurados com sucesso!");
                }
                return Result.Ok("Arquivo de banco (.db) importado! (Dica: use os arquivos .ordisbak da pasta Backups para trazer também o usuário, senha e 3 palavras-chave).");
            }

            return Result.Fail("Formato não suportado. Selecione uma cápsula '.ordisbak' ou '.db'.");
        }
        catch (Exception ex)
        {
            return Result.Fail($"Falha ao restaurar backup: {ex.Message}");
        }
    }
}
