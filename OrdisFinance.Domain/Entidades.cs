namespace OrdisFinance.Domain;

public enum TipoTransacao { Entrada = 1, Saida = 2 }

public class Transacao
{
    public int Id { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public decimal ValorBruto { get; set; }
    public decimal ValorDescontos { get; set; }
    public DateTime Data { get; set; }
    public TipoTransacao Tipo { get; set; }
    public string Categoria { get; set; } = "Fixos";
    public string FormaPagamento { get; set; } = "Pix";
    public string ParcelaInfo { get; set; } = string.Empty;
    public string OrigemArquivo { get; set; } = "Manual";
    public string TitularNome { get; set; } = string.Empty;
    public string TitularCpf { get; set; } = string.Empty;
    public string NaturezaReceita { get; set; } = string.Empty;
}

public class ItemParcelamentoFuturo
{
    public string Descricao { get; set; } = string.Empty;
    public decimal ValorParcela { get; set; }
    public int ParcelaAtualNaReferencia { get; set; }
    public int TotalParcelas { get; set; }
    public int ParcelasRestantesAposMes { get; set; }
    public decimal SaldoRestanteTotal { get; set; }
    public string MesTermino { get; set; } = string.Empty;
    public bool JaLancadaNoMesReferencia { get; set; }
}

public class ResumoParcelamentosFuturos
{
    public decimal ValorParcelasJaLancadasNoMes { get; set; }
    public decimal ValorParcelasProjetadasNoMes { get; set; }
    public decimal CompromissoTotalParcelasNoMes => ValorParcelasJaLancadasNoMes + ValorParcelasProjetadasNoMes;
    public decimal SaldoTotalRestanteGeral { get; set; }
    public int QtdContratosAtivos { get; set; }
    public List<ItemParcelamentoFuturo> ItensAtivos { get; set; } = new();
}

public class PerfilContracheque
{
    public int Id { get; set; }
    public string NomeTitular { get; set; } = string.Empty;
    public string CpfTitular { get; set; } = string.Empty;
    public decimal ValorBrutoPadrao { get; set; }
    public decimal ValorDescontosPadrao { get; set; }
    public decimal ValorLiquidoPadrao { get; set; }
    public string DescricaoPadrao { get; set; } = "Contracheque Mensal";
}

public class DadosContrachequeExtraido
{
    public string NomeTitular { get; set; } = string.Empty;
    public string CpfTitular { get; set; } = string.Empty;
    public decimal ValorBruto { get; set; }
    public decimal ValorDescontos { get; set; }
    public decimal ValorLiquido { get; set; }
    public string DescricaoSugerida { get; set; } = "Contracheque Mensal";
}

public class SugestaoDescricaoHistorico
{
    public string Descricao { get; set; } = string.Empty;
    public string Categoria { get; set; } = "Triviais";
    public string FormaPagamento { get; set; } = "Pix";
    public decimal UltimoValor { get; set; }
}

public class CaixinhaPlanejamento
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public decimal ValorAlvo { get; set; }
    public decimal ValorAtual { get; set; }
    public DateTime DataMeta { get; set; } = DateTime.Now.AddMonths(6);
    public string Prioridade { get; set; } = "Alta";
}

public class ItemQuarentena
{
    public int Id { get; set; }
    public string CaminhoArquivo { get; set; } = string.Empty;
    public string TextoExtraidoBruto { get; set; } = string.Empty;
    public string DescricaoSugerida { get; set; } = string.Empty;
    public decimal ValorSugerido { get; set; }
    public DateTime DataSugerida { get; set; }
    public string CategoriaSugerida { get; set; } = string.Empty;
    public bool Aprovado { get; set; }
}

public class EventoSazonal
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public decimal ValorAlvo { get; set; }
    public DateTime DataVencimento { get; set; }
    public int MesesAntecedenciaAlerta { get; set; } = 4;
}

public class RegraAprendida
{
    public int Id { get; set; }
    public string PadraoEstabelecimento { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
}

public class ItemFaturaExtraido
{
    public string CartaoFinal { get; set; } = "Geral";
    public string DataCompra { get; set; } = string.Empty;
    public string Estabelecimento { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string ParcelaInfo { get; set; } = "À vista";
    public string CategoriaSugerida { get; set; } = "Triviais";
    public bool ConhecidoPeloOrdis { get; set; }
}

public class ResultadoExtracaoFatura
{
    public decimal ValorTotalFatura { get; set; }
    public decimal TotalCreditosAbatidos { get; set; }
    public DateTime DataVencimento { get; set; } = DateTime.Now;
    public List<ItemFaturaExtraido> Itens { get; set; } = new();
}
