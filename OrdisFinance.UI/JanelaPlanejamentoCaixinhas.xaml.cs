using Dapper;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public class CaixinhaViewModel : CaixinhaPlanejamento
{
    public double PercentualConcluido { get => ValorAlvo > 0 ? Math.Min(100.0, (double)(ValorAtual / ValorAlvo) * 100.0) : 0.0; set { } }
    public string TituloExibicao => $"🎯 {Titulo.ToUpper()} [{Prioridade}]";
    public string ProgressoTexto => $"R$ {ValorAtual:N2} / R$ {ValorAlvo:N2} ({PercentualConcluido:N0}%)";

    public string DetalheFalta
    {
        get
        {
            decimal falta = Math.Max(0m, ValorAlvo - ValorAtual);
            return falta > 0
                ? $"Faltam R$ {falta:N2} • Meta: {DataMeta:dd/MM/yyyy}"
                : $"✔ META ATINGIDA! ({DataMeta:dd/MM/yyyy})";
        }
    }

    public string AporteSugeridoTexto
    {
        get
        {
            decimal falta = Math.Max(0m, ValorAlvo - ValorAtual);
            if (falta <= 0) return "Concluída!";
            int meses = Math.Max(1, ((DataMeta.Year - DateTime.Now.Year) * 12) + DataMeta.Month - DateTime.Now.Month);
            decimal porMes = Math.Round(falta / meses, 2);
            return $"Ritmo: R$ {porMes:N2}/mês";
        }
    }
}

public partial class JanelaPlanejamentoCaixinhas : Window
{
    private readonly CofreSqliteService _cofre;
    private readonly MiniOrdisAdvisorService _advisor;
    private readonly string _anoMesReferencia;
    private int _idEmEdicao = 0;
    private bool _formatando = false;
    public bool HouveAlteracao { get; private set; } = false;

    public JanelaPlanejamentoCaixinhas(CofreSqliteService cofre, MiniOrdisAdvisorService advisor, string anoMesReferencia)
    {
        InitializeComponent();
        _cofre = cofre;
        _advisor = advisor;
        _anoMesReferencia = anoMesReferencia;

        ComboPrioridadeCaixinha.ItemsSource = new List<string> { "Alta", "Média", "Baixa" };
        ComboPrioridadeCaixinha.SelectedIndex = 0;
        TxtDataCaixinha.Text = DateTime.Now.AddMonths(8).ToString("dd/MM/yyyy");

        RecarregarCaixinhasEEstrategia();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void RecarregarCaixinhasEEstrategia()
    {
        var lista = _cofre.ObterCaixinhas()
            .Select(c => new CaixinhaViewModel
            {
                Id = c.Id,
                Titulo = c.Titulo,
                ValorAlvo = c.ValorAlvo,
                ValorAtual = c.ValorAtual,
                DataMeta = c.DataMeta,
                Prioridade = c.Prioridade
            }).ToList();

        ListaCaixinhas.ItemsSource = lista;
        TxtRelatorioEstrategico.Text = _advisor.GerarPlanoEstrategicoCompleto(_anoMesReferencia);
    }

    private void ListaCaixinhas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaCaixinhas.SelectedItem is not CaixinhaViewModel cx)
            return;

        _idEmEdicao = cx.Id;
        TxtTituloCaixinha.Text = cx.Titulo;
        ComboPrioridadeCaixinha.SelectedItem = cx.Prioridade;

        _formatando = true;
        TxtValorAlvoCaixinha.Text = cx.ValorAlvo.ToString("N2", new CultureInfo("pt-BR"));
        TxtValorAtualCaixinha.Text = cx.ValorAtual > 0 ? cx.ValorAtual.ToString("N2", new CultureInfo("pt-BR")) : "";
        TxtDataCaixinha.Text = cx.DataMeta.ToString("dd/MM/yyyy");
        _formatando = false;

        BtnSalvarCaixinha.Content = "✔ ATUALIZAR CAIXINHA";
    }

    private void BtnLimparCaixinha_Click(object sender, RoutedEventArgs e)
    {
        _idEmEdicao = 0;
        ListaCaixinhas.SelectedIndex = -1;
        TxtTituloCaixinha.Clear();
        _formatando = true;
        TxtValorAlvoCaixinha.Clear();
        TxtValorAtualCaixinha.Clear();
        TxtDataCaixinha.Text = DateTime.Now.AddMonths(8).ToString("dd/MM/yyyy");
        _formatando = false;
        ComboPrioridadeCaixinha.SelectedIndex = 0;
        BtnSalvarCaixinha.Content = "✔ SALVAR CAIXINHA";
    }

    private void ApenasDigitos_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void CampoMoeda_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatando || sender is not TextBox tb) return;
        _formatando = true;
        try
        {
            string d = Regex.Replace(tb.Text ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(d) || !long.TryParse(d, out long centavos) || centavos == 0)
                tb.Text = string.Empty;
            else
            {
                tb.Text = (centavos / 100m).ToString("N2", new CultureInfo("pt-BR"));
                tb.CaretIndex = tb.Text.Length;
            }
        }
        finally { _formatando = false; }
    }

    private void TxtDataCaixinha_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatando) return;
        _formatando = true;
        try
        {
            string d = Regex.Replace(TxtDataCaixinha.Text ?? "", @"[^\d]", "");
            if (d.Length > 8) d = d.Substring(0, 8);
            TxtDataCaixinha.Text = d.Length switch
            {
                <= 2 => d,
                <= 4 => $"{d.Substring(0, 2)}/{d.Substring(2)}",
                _ => $"{d.Substring(0, 2)}/{d.Substring(2, 2)}/{d.Substring(4)}"
            };
            TxtDataCaixinha.CaretIndex = TxtDataCaixinha.Text.Length;
        }
        finally { _formatando = false; }
    }

    private decimal LerDecimal(string? t)
    {
        decimal.TryParse((t ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v);
        return v;
    }

    private void BtnSalvarCaixinha_Click(object sender, RoutedEventArgs e)
    {
        string titulo = TxtTituloCaixinha.Text.Trim();
        decimal alvo = LerDecimal(TxtValorAlvoCaixinha.Text);
        decimal atual = LerDecimal(TxtValorAtualCaixinha.Text);
        string prio = ComboPrioridadeCaixinha.SelectedItem?.ToString() ?? "Alta";

        if (string.IsNullOrWhiteSpace(titulo) || alvo <= 0)
        {
            JanelaMensagem.Exibir("Informe o Nome da Caixinha (ex: Caixinha para a Viagem) e o Valor Pré-Definido maior que zero!", "Mini-Ordis // Planejamento", false, this);
            return;
        }

        if (!DateTime.TryParseExact(TxtDataCaixinha.Text.Trim(), "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dtMeta))
            dtMeta = DateTime.Now.AddMonths(8);

        _cofre.SalvarCaixinha(new CaixinhaPlanejamento
        {
            Id = _idEmEdicao,
            Titulo = titulo,
            ValorAlvo = alvo,
            ValorAtual = atual,
            DataMeta = dtMeta,
            Prioridade = prio
        });

        HouveAlteracao = true;
        BtnLimparCaixinha_Click(sender, e);
        RecarregarCaixinhasEEstrategia();
        TxtStatusCaixinhas.Text = $"✔ Caixinha '{titulo}' salva e analisada pelo Estrategista do Mini-Ordis!";
    }

    private void BtnAporteRapido_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCaixinhas.SelectedItem is not CaixinhaViewModel cx)
        {
            JanelaMensagem.Exibir("Selecione uma Caixinha na lista acima para guardar o valor!", "Mini-Ordis // Caixinhas", false, this);
            return;
        }

        decimal aporte = LerDecimal(TxtAporteRapido.Text);
        if (aporte <= 0)
        {
            JanelaMensagem.Exibir("Digite um valor maior que zero na caixa de aporte rápido!", "Mini-Ordis", false, this);
            return;
        }

        cx.ValorAtual += aporte;
        _cofre.SalvarCaixinha(cx);

        _formatando = true;
        TxtAporteRapido.Clear();
        _formatando = false;

        HouveAlteracao = true;
        RecarregarCaixinhasEEstrategia();
        TxtStatusCaixinhas.Text = $"✔ +R$ {aporte:N2} guardados em '{cx.Titulo}'! Saldo atual: R$ {cx.ValorAtual:N2}.";
    }

    private void BtnConverterCaixinha_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCaixinhas.SelectedItem is not CaixinhaViewModel cx)
        {
            JanelaMensagem.Exibir("Selecione uma Caixinha na lista acima para converter em compra/lançamento no Cofre!", "Mini-Ordis // Converter Caixinha", false, this);
            return;
        }

        decimal valorConverter = cx.ValorAtual > 0 ? cx.ValorAtual : cx.ValorAlvo;
        if (valorConverter <= 0)
        {
            JanelaMensagem.Exibir("Esta Caixinha não possui valor definido para conversão.", "Mini-Ordis", false, this);
            return;
        }

        string msgConfirmacao =
            $"⚡ CONVERSÃO DE CAIXINHA EM LANÇAMENTO REAL:\n\n" +
            $"• Caixinha: {cx.Titulo}\n" +
            $"• Valor a lançar no Cofre: R$ {valorConverter:N2}\n\n" +
            $"Deseja registrar esta conquista como lançamento confirmado no Cofre e abater o saldo acumulado da caixinha?";

        if (!JanelaMensagem.Exibir(msgConfirmacao, "CONFIRMAR CONVERSÃO NO COFRE", confirmarSimNao: true, owner: this))
            return;

        string categoriaMeta = "Planejamento e Metas";
        _cofre.GarantirCategoria(categoriaMeta, "Operador");

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            INSERT INTO Transacoes (Descricao, Valor, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo)
            VALUES (@Desc, @Val, @Data, 2, @Cat, 'Pix', 'À vista', 'Caixinha');",
            new
            {
                Desc = $"[Conquista Caixinha] {cx.Titulo}",
                Val = valorConverter,
                Data = DateTime.Now.ToString("yyyy-MM-dd"),
                Cat = categoriaMeta
            });

        cx.ValorAtual = Math.Max(0m, cx.ValorAtual - valorConverter);
        _cofre.SalvarCaixinha(cx);

        HouveAlteracao = true;
        RecarregarCaixinhasEEstrategia();
        TxtStatusCaixinhas.Text = $"✔ Caixinha '{cx.Titulo}' (R$ {valorConverter:N2}) convertida em lançamento real no Cofre!";
    }

    private void BtnExcluirCaixinha_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCaixinhas.SelectedItem is not CaixinhaViewModel cx)
        {
            JanelaMensagem.Exibir("Selecione uma Caixinha na lista para excluir!", "Mini-Ordis", false, this);
            return;
        }

        if (!JanelaMensagem.Exibir($"Deseja excluir a caixinha '{cx.Titulo}'?", "CONFIRMAR EXCLUSÃO", true, this))
            return;

        _cofre.ExcluirCaixinha(cx.Id);
        HouveAlteracao = true;
        BtnLimparCaixinha_Click(sender, e);
        RecarregarCaixinhasEEstrategia();
    }

    private void BtnRecalcularEstrategia_Click(object sender, RoutedEventArgs e)
    {
        RecarregarCaixinhasEEstrategia();
        TxtStatusCaixinhas.Text = "⚡ Estratégia neural recalculada com base em todos os seus lançamentos e caixinhas!";
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = HouveAlteracao;
        Close();
    }
}


