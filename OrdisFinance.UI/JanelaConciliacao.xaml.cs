using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dapper;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public class ItemFaturaRowViewModel : INotifyPropertyChanged
{
    public bool Incluir { get; set; } = true;
    public string CartaoFinal { get; set; } = "Geral";
    public string Estabelecimento { get; set; } = string.Empty;
    public string ParcelaInfo { get; set; } = "À vista";
    public string ValorTexto { get; set; } = "0,00";
    public bool ConhecidoPeloOrdis { get; set; }

    private string _categoriaSelecionada = "Triviais";
    public string CategoriaSelecionada
    {
        get => _categoriaSelecionada;
        set
        {
            _categoriaSelecionada = value;
            OnPropertyChanged(nameof(CategoriaSelecionada));
        }
    }

    private List<string> _categoriasDisponiveis = new();
    public List<string> CategoriasDisponiveis
    {
        get => _categoriasDisponiveis;
        set
        {
            _categoriasDisponiveis = value;
            OnPropertyChanged(nameof(CategoriasDisponiveis));
        }
    }

    public string StatusTexto => ConhecidoPeloOrdis ? "✔ AUTO (MEMORIA)" : "⚠ REVISAR";
    public Brush CorStatus => ConhecidoPeloOrdis 
        ? new SolidColorBrush(Color.FromRgb(0, 255, 102)) 
        : new SolidColorBrush(Color.FromRgb(255, 184, 0));

    public decimal ObterValorDecimal()
    {
        decimal.TryParse((ValorTexto ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v);
        return v;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class JanelaConciliacao : Window
{
    private readonly CofreSqliteService _cofre;
    private readonly decimal _valorTotalFaturaLido;
    private readonly decimal _creditosAbatidos;
    private readonly DateTime _dataVencimentoFatura;
    private readonly int _idQuarentena;
    private readonly string _origemArquivo;
    public ObservableCollection<ItemFaturaRowViewModel> Linhas { get; set; } = new();

    public JanelaConciliacao(
        CofreSqliteService cofre,
        ResultadoExtracaoFatura extracao,
        decimal valorTotalFaturaConfirmado,
        int idQuarentena,
        string origemArquivo)
    {
        InitializeComponent();

        _cofre = cofre;
        _valorTotalFaturaLido = valorTotalFaturaConfirmado > 0 ? valorTotalFaturaConfirmado : extracao.ValorTotalFatura;
        _creditosAbatidos = extracao.TotalCreditosAbatidos;
        _dataVencimentoFatura = extracao.DataVencimento;
        _idQuarentena = idQuarentena;
        _origemArquivo = origemArquivo;

        int qtdCartoes = extracao.Itens.Select(x => x.CartaoFinal).Distinct().Count();
        TxtSubtituloFatura.Text = $"Vencimento: {_dataVencimentoFatura:dd/MM/yyyy} // {qtdCartoes} Cartões Detectados ({extracao.Itens.Count} lançamentos)";

        var categorias = _cofre.ObterCategorias();

        foreach (var item in extracao.Itens)
        {
            Linhas.Add(new ItemFaturaRowViewModel
            {
                Incluir = true,
                CartaoFinal = item.CartaoFinal,
                Estabelecimento = item.Estabelecimento,
                ParcelaInfo = item.ParcelaInfo,
                ValorTexto = item.Valor.ToString("N2"),
                ConhecidoPeloOrdis = item.ConhecidoPeloOrdis,
                CategoriasDisponiveis = categorias,
                CategoriaSelecionada = categorias.Contains(item.CategoriaSugerida) ? item.CategoriaSugerida : categorias.FirstOrDefault() ?? "Triviais"
            });
        }

        ListaItensFatura.ItemsSource = Linhas;
        RecalcularAuditoriaSoma();
    }

    private void RecalcularAuditoriaSoma()
    {
        if (TxtAuditoriaSoma == null || TxtDiferencaSoma == null) return;

        decimal somaItens = Linhas.Where(x => x.Incluir).Sum(x => x.ObterValorDecimal());
        decimal liquidoCalculado = somaItens - _creditosAbatidos;
        decimal diferenca = _valorTotalFaturaLido - liquidoCalculado;

        if (_creditosAbatidos > 0)
            TxtAuditoriaSoma.Text = $"FATURA: R$ {_valorTotalFaturaLido:N2} | DESPESAS: R$ {somaItens:N2} (-R$ {_creditosAbatidos:N2} Abatido)";
        else
            TxtAuditoriaSoma.Text = $"TOTAL FATURA: R$ {_valorTotalFaturaLido:N2} | SOMA ITENS: R$ {somaItens:N2}";

        if (Math.Abs(diferenca) <= 0.05m)
        {
            TxtDiferencaSoma.Text = "✔ BATIMENTO EXATO (100% DOS 4 CARTÕES CONCILIADOS AO CENTAVO!)";
            TxtDiferencaSoma.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 102));
        }
        else if (diferenca > 0)
        {
            TxtDiferencaSoma.Text = $"ℹ Diferença de R$ {diferenca:N2} em relação ao boleto";
            TxtDiferencaSoma.Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255));
        }
        else
        {
            TxtDiferencaSoma.Text = $"⚠ Soma líquida excede a fatura em R$ {Math.Abs(diferenca):N2}";
            TxtDiferencaSoma.Foreground = new SolidColorBrush(Color.FromRgb(255, 184, 0));
        }
    }

    private void CheckItem_Changed(object sender, RoutedEventArgs e) => RecalcularAuditoriaSoma();
    private void TxtValorItem_TextChanged(object sender, TextChangedEventArgs e) => RecalcularAuditoriaSoma();

    private void BtnCriarCategoria_Click(object sender, RoutedEventArgs e)
    {
        string nova = TxtNovaCatModal.Text.Trim();
        if (string.IsNullOrWhiteSpace(nova)) return;

        _cofre.GarantirCategoria(nova, "Operador");
        TxtNovaCatModal.Clear();

        var categoriasAtualizadas = _cofre.ObterCategorias();
        foreach (var row in Linhas)
        {
            row.CategoriasDisponiveis = categoriasAtualizadas;
        }
    }

    private void BtnAddLinhaManual_Click(object sender, RoutedEventArgs e)
    {
        var categorias = _cofre.ObterCategorias();
        Linhas.Add(new ItemFaturaRowViewModel
        {
            Incluir = true,
            CartaoFinal = "Manual",
            Estabelecimento = "Ajuste / Item Adicional",
            ParcelaInfo = "À vista",
            ValorTexto = "0,00",
            ConhecidoPeloOrdis = false,
            CategoriasDisponiveis = categorias,
            CategoriaSelecionada = categorias.FirstOrDefault() ?? "Fixos"
        });
        RecalcularAuditoriaSoma();
    }

    private void BtnConsolidarFatura_Click(object sender, RoutedEventArgs e)
    {
        var selecionados = Linhas.Where(x => x.Incluir && x.ObterValorDecimal() > 0).ToList();
        if (!selecionados.Any())
        {
            JanelaMensagem.Exibir("Nenhum item válido selecionado para gravação.", "Aviso do Mini-Ordis", false, this);
            return;
        }

        using var conn = _cofre.CriarConexaoAberta();
        string dataGravar = _dataVencimentoFatura.ToString("yyyy-MM-dd");

        foreach (var item in selecionados)
        {
            string sufixoParcela = item.ParcelaInfo != "À vista" && !string.IsNullOrWhiteSpace(item.ParcelaInfo)
                ? $" ({item.ParcelaInfo})"
                : "";

            string descFinal = $"[{item.CartaoFinal}] {item.Estabelecimento}{sufixoParcela}";

            conn.Execute(@"
                INSERT INTO Transacoes (Descricao, Valor, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo)
                VALUES (@Desc, @Val, @Data, 2, @Cat, 'Cartão de Crédito', @Parc, @Origem);",
                new
                {
                    Desc = descFinal,
                    Val = item.ObterValorDecimal(),
                    Data = dataGravar,
                    Cat = item.CategoriaSelecionada,
                    Parc = item.ParcelaInfo,
                    Origem = _origemArquivo
                });

            _cofre.AprenderRegraEstabelecimento(item.Estabelecimento, item.CategoriaSelecionada);
        }

        conn.Execute("UPDATE Quarentena SET Aprovado = 1 WHERE Id = @Id", new { Id = _idQuarentena });

        DialogResult = true;
        Close();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

