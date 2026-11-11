using System.Text;
using Dapper;
using OrdisFinance.Domain;

namespace OrdisFinance.Infrastructure;

public class DiagnosticoOrdis
{
    public decimal GastoTotalMesAtual { get; set; }
    public decimal PercentualCaixaConsumido { get; set; }
    public string NivelEscudo { get; set; } = "VERDE";
    public string MensagemConselho { get; set; } = string.Empty;
    public List<string> AlertasSazonais { get; set; } = new();
}

public class ResumoCategoriaEstrategia
{
    public string Categoria { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class MiniOrdisAdvisorService
{
    private readonly CofreSqliteService _cofre;

    public MiniOrdisAdvisorService(CofreSqliteService cofre)
    {
        _cofre = cofre;
    }

    public DiagnosticoOrdis GerarDiagnosticoMensal(decimal rendaMensalEstimada, decimal metaReservaEscudo, string? anoMesOpcional = null)
    {
        string prefixoMes = string.IsNullOrWhiteSpace(anoMesOpcional) ? DateTime.Now.ToString("yyyy-MM") : anoMesOpcional;

        var balanco = _cofre.CalcularBalancoMes(prefixoMes);
        var parcelas = _cofre.CalcularParcelamentosFuturos(prefixoMes);

        // Soma os gastos ja lancados no mes + as parcelas herdadas de meses anteriores que cairao neste mes!
        decimal gastoComprometidoTotal = balanco.GastosLiquidos + parcelas.ValorParcelasProjetadasNoMes;

        decimal tetoSeguro = rendaMensalEstimada - metaReservaEscudo;
        decimal percentual = rendaMensalEstimada > 0 ? Math.Round((gastoComprometidoTotal / rendaMensalEstimada) * 100, 1) : 0;

        var diag = new DiagnosticoOrdis
        {
            GastoTotalMesAtual = gastoComprometidoTotal,
            PercentualCaixaConsumido = percentual
        };

        if (gastoComprometidoTotal >= tetoSeguro || percentual >= 90m)
        {
            diag.NivelEscudo = "VERMELHO";
            diag.MensagemConselho = $"SOBRECARGA CRÍTICA ({percentual}%): O caixa atingiu o limite de segurança. Bloqueie gastos triviais imediatamente!";
        }
        else if (percentual >= 75m)
        {
            diag.NivelEscudo = "AMARELO";
            diag.MensagemConselho = $"ATENÇÃO TÁTICA ({percentual}%): Escudo em alerta amarelo. Reduza supérfluos para proteger as Caixinhas e a reserva!";
        }
        else
        {
            diag.NivelEscudo = "VERDE";
            diag.MensagemConselho = $"SISTEMAS ESTÁVEIS ({percentual}%): Operação dentro dos parâmetros. Margem excelente para alimentar suas Caixinhas!";
        }

        try
        {
            // 1. Destaque de Parcelamentos Futuros no Radar!
            if (parcelas.SaldoTotalRestanteGeral > 0 || parcelas.CompromissoTotalParcelasNoMes > 0)
            {
                diag.AlertasSazonais.Add(
                    $"💳 [PARCELAMENTOS ATIVOS ({parcelas.QtdContratosAtivos})]: R$ {parcelas.CompromissoTotalParcelasNoMes:N2} neste mês | Falta quitar R$ {parcelas.SaldoTotalRestanteGeral:N2} no total futuro");
            }

            // 2. Alertas do Calendario Sazonal
            var eventos = _cofre.ObterEventosSazonais();
            foreach (var ev in eventos)
            {
                int mesesRestantes = ((ev.DataVencimento.Year - DateTime.Now.Year) * 12) + ev.DataVencimento.Month - DateTime.Now.Month;
                if (mesesRestantes >= 0 && mesesRestantes <= ev.MesesAntecedenciaAlerta)
                {
                    decimal parcelaSugerida = mesesRestantes == 0 ? ev.ValorAlvo : Math.Round(ev.ValorAlvo / mesesRestantes, 2);
                    diag.AlertasSazonais.Add($"📅 [SAZONAL {ev.DataVencimento:dd/MM/yy}] {ev.Titulo}: reservar R$ {parcelaSugerida:N2}/mês (Alvo: R$ {ev.ValorAlvo:N2})");
                }
            }

            // 3. Resumo das Caixinhas
            var caixinhas = _cofre.ObterCaixinhas();
            foreach (var cx in caixinhas)
            {
                decimal falta = Math.Max(0m, cx.ValorAlvo - cx.ValorAtual);
                if (falta > 0)
                {
                    int mesesMeta = Math.Max(1, ((cx.DataMeta.Year - DateTime.Now.Year) * 12) + cx.DataMeta.Month - DateTime.Now.Month);
                    decimal aporteMes = Math.Round(falta / mesesMeta, 2);
                    diag.AlertasSazonais.Add($"🎯 [CAIXINHA] {cx.Titulo}: guardado R$ {cx.ValorAtual:N2} de R$ {cx.ValorAlvo:N2} (Meta: R$ {aporteMes:N2}/mês)");
                }
                else
                {
                    diag.AlertasSazonais.Add($"✔ [CAIXINHA CONCLUÍDA] {cx.Titulo}: Meta de R$ {cx.ValorAlvo:N2} atingida!");
                }
            }
        }
        catch { }

        if (!diag.AlertasSazonais.Any())
        {
            diag.AlertasSazonais.Add("Nenhum parcelamento, evento sazonal ou caixinha pendente no horizonte.");
        }

        return diag;
    }

    public string GerarPlanoEstrategicoCompleto(string prefixoAnoMes)
    {
        try
        {
            var sb = new StringBuilder();
            using var conn = _cofre.CriarConexaoAberta();

            var balanco = _cofre.CalcularBalancoMes(prefixoAnoMes);
            var infoRenda = _cofre.ObterRendaOuMedia3Meses(prefixoAnoMes);
            var parcelas = _cofre.CalcularParcelamentosFuturos(prefixoAnoMes);

            decimal renda = infoRenda.RendaReferencia;
            decimal gastosJaLancados = balanco.GastosLiquidos;
            decimal gastosComParcelasFuturas = gastosJaLancados + parcelas.ValorParcelasProjetadasNoMes;
            decimal escudo10 = Math.Round(renda * 0.10m, 2);
            decimal margemFuturaLivre = renda - gastosComParcelasFuturas - escudo10;

            string descOrigemRenda = infoRenda.EhMedia3Meses
                ? $"Média Móvel de {Math.Max(1, infoRenda.QtdMesesMedia)} mês(es) (Sem holerite lançado em {prefixoAnoMes})"
                : $"Entradas Reais Lançadas em {prefixoAnoMes}";

            sb.AppendLine($"[TELEMETRIA PREDITIVA DO MINI-ORDIS // MÊS ALVO: {prefixoAnoMes}]");
            sb.AppendLine($"• Base de Caixa: R$ {renda:N2} ({descOrigemRenda})");
            sb.AppendLine($"• Gastos Lançados no Mês: R$ {gastosJaLancados:N2}" +
                          (parcelas.ValorParcelasProjetadasNoMes > 0 ? $" + R$ {parcelas.ValorParcelasProjetadasNoMes:N2} (Parcelas herdadas de faturas anteriores)" : ""));
            sb.AppendLine($"• Margem Livre p/ Gastar no Mês (após Escudo 10% e Parcelas): R$ {margemFuturaLivre:N2}");
            sb.AppendLine();

            // SECAO NOVA: RADAR DETALHADO DE PARCELAMENTOS FUTUROS
            sb.AppendLine("💳 [RADAR DE PARCELAMENTOS FUTUROS // QUANTO FALTA PAGAR]:");
            if (parcelas.ItensAtivos.Any())
            {
                sb.AppendLine($"• Compromisso com Parcelas em {prefixoAnoMes}: R$ {parcelas.CompromissoTotalParcelasNoMes:N2}/mês ({parcelas.QtdContratosAtivos} compras parceladas ativas)");
                sb.AppendLine($"• SALDO TOTAL QUE FALTA PARA QUITAR TODOS OS PARCELAMENTOS: R$ {parcelas.SaldoTotalRestanteGeral:N2}");
                foreach (var p in parcelas.ItensAtivos.OrderByDescending(x => x.SaldoRestanteTotal).Take(8))
                {
                    string statusMes = p.JaLancadaNoMesReferencia ? "já na fatura deste mês" : "projetada p/ este mês";
                    sb.AppendLine($"   ▸ {p.Descricao}: Parcela {p.ParcelaAtualNaReferencia:D2}/{p.TotalParcelas:D2} de R$ {p.ValorParcela:N2} ({statusMes}) → Faltam {p.ParcelasRestantesAposMes}x (Total restante: R$ {p.SaldoRestanteTotal:N2} | Quita em {p.MesTermino})");
                }
            }
            else
            {
                sb.AppendLine("• Nenhum parcelamento futuro pendente detectado! 100% da sua margem mensal está livre de dívidas parceladas.");
            }
            sb.AppendLine();

            // Analise de Categorias Variaveis
            var gastosPorCat = conn.Query<ResumoCategoriaEstrategia>(@"
                SELECT Categoria, COALESCE(SUM(Valor), 0) AS Total
                FROM Transacoes
                WHERE Tipo = 2 AND substr(Data, 1, 7) = @Ym
                GROUP BY Categoria
                ORDER BY Total DESC", new { Ym = prefixoAnoMes }).ToList();

            var variaveis = gastosPorCat.Where(c =>
                (c.Categoria ?? "").Equals("Triviais", StringComparison.OrdinalIgnoreCase) ||
                (c.Categoria ?? "").Equals("Lanches", StringComparison.OrdinalIgnoreCase) ||
                (c.Categoria ?? "").Equals("jogos", StringComparison.OrdinalIgnoreCase) ||
                (c.Categoria ?? "").Equals("Assinaturas", StringComparison.OrdinalIgnoreCase)).ToList();

            decimal somaVariaveis = variaveis.Sum(v => v.Total);
            decimal potencialOtimizacao = Math.Round(somaVariaveis * 0.25m, 2);

            if (variaveis.Any())
            {
                var maiorVar = variaveis.First();
                sb.AppendLine("⚡ [DIAGNÓSTICO DE OTIMIZAÇÃO DE CATEGORIAS]:");
                sb.AppendLine($"• Gastos flexíveis em {prefixoAnoMes}: R$ {somaVariaveis:N2} (Maior: '{maiorVar.Categoria}' com R$ {maiorVar.Total:N2}).");
                sb.AppendLine($"• Redirecionar 25% libera +R$ {potencialOtimizacao:N2}/mês para quitar parcelas ou acelerar suas Caixinhas!");
                sb.AppendLine();
            }

            // Plano Tatico das Caixinhas
            var caixinhas = _cofre.ObterCaixinhas();
            sb.AppendLine("🎯 [PLANO TÁTICO PARA AS SUAS CAIXINHAS]:");

            decimal capacidadeAporteReal = Math.Max(0m, margemFuturaLivre) + potencialOtimizacao;
            if (capacidadeAporteReal <= 0)
                capacidadeAporteReal = Math.Max(150m, potencialOtimizacao);

            foreach (var cx in caixinhas)
            {
                decimal falta = Math.Max(0m, cx.ValorAlvo - cx.ValorAtual);
                if (falta <= 0)
                {
                    sb.AppendLine($"✔ {cx.Titulo.ToUpper()}: META CONCLUÍDA (R$ {cx.ValorAtual:N2} / R$ {cx.ValorAlvo:N2})!");
                    continue;
                }

                int mesesAteData = Math.Max(1, ((cx.DataMeta.Year - DateTime.Now.Year) * 12) + cx.DataMeta.Month - DateTime.Now.Month);
                decimal necessarioPorMes = Math.Round(falta / mesesAteData, 2);

                double pesoPrioridade = cx.Prioridade == "Alta" ? 0.60 : (cx.Prioridade == "Média" ? 0.40 : 0.25);
                decimal aporteSugeridoOrdis = Math.Round(capacidadeAporteReal * (decimal)pesoPrioridade, 2);
                if (aporteSugeridoOrdis <= 0) aporteSugeridoOrdis = necessarioPorMes;

                int mesesNoRitmoOrdis = aporteSugeridoOrdis > 0 ? (int)Math.Ceiling(falta / aporteSugeridoOrdis) : mesesAteData;

                sb.AppendLine($"▸ {cx.Titulo.ToUpper()} (Prioridade {cx.Prioridade}):");
                sb.AppendLine($"   • Valor Pré-Definido: R$ {cx.ValorAlvo:N2} | Guardado: R$ {cx.ValorAtual:N2} | Faltam: R$ {falta:N2}");
                sb.AppendLine($"   • Estratégia Mini-Ordis: Guardando R$ {aporteSugeridoOrdis:N2}/mês da sua margem futura, você conclui em ~{mesesNoRitmoOrdis} mês(es)!");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[AVISO DE TELEMETRIA]: Calculando projeção trimestral e parcelamentos... ({ex.Message})";
        }
    }
}
