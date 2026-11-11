using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dapper;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public class RegistroCofreItemViewModel : Transacao
{
    public string DataTexto => Data.ToString("dd/MM/yyyy");
    public string TipoBadge => Tipo == TipoTransacao.Entrada ? "ENTRADA" : "SAÍDA";

    public Brush CorTipo => Tipo == TipoTransacao.Entrada
        ? new SolidColorBrush(Color.FromRgb(0, 255, 102))
        : new SolidColorBrush(Color.FromRgb(255, 0, 85));

    public Brush FundoTipo => Tipo == TipoTransacao.Entrada
        ? new SolidColorBrush(Color.FromRgb(8, 30, 18))
        : new SolidColorBrush(Color.FromRgb(34, 10, 20));

    public string DescricaoExibicao =>
        !string.IsNullOrWhiteSpace(TitularNome) && Tipo == TipoTransacao.Entrada
            ? $"{Descricao} ({TitularNome})"
            : Descricao;

    public string ValorFormatado => Tipo == TipoTransacao.Entrada
        ? $"+ R$ {Valor:N2}"
        : $"R$ {Valor:N2}";

    public Brush CorValor => Tipo == TipoTransacao.Entrada
        ? new SolidColorBrush(Color.FromRgb(0, 255, 102))
        : new SolidColorBrush(Color.FromRgb(224, 248, 255));
}

public partial class JanelaRegistrosCofre : Window
{
    private readonly CofreSqliteService _cofre;
    private List<RegistroCofreItemViewModel> _todosRegistros = new();
    private List<RegistroCofreItemViewModel> _registrosFiltrados = new();
    private RegistroCofreItemViewModel? _itemEmEdicao;
    private bool _inicializando = true;
    private bool _formatandoCampo = false;
    public bool HouveAlteracao { get; private set; } = false;

    public JanelaRegistrosCofre(CofreSqliteService cofre, int mesInicial, string anoInicial)
    {
        InitializeComponent();
        _cofre = cofre;

        ComboFiltroMes.ItemsSource = new List<string>
        {
            "Todos os Meses",
            "01 - Jan", "02 - Fev", "03 - Mar", "04 - Abr", "05 - Mai", "06 - Jun",
            "07 - Jul", "08 - Ago", "09 - Set", "10 - Out", "11 - Nov", "12 - Dez"
        };
        ComboFiltroMes.SelectedIndex = Math.Clamp(mesInicial, 0, 12);

        var anos = new List<string> { "Todos", "2025", "2026", "2027" };
        ComboFiltroAno.ItemsSource = anos;
        ComboFiltroAno.SelectedItem = anos.Contains(anoInicial) ? anoInicial : "Todos";

        RecarregarCategoriasFiltroEEdicao();

        ComboFiltroMetodo.ItemsSource = new List<string>
        {
            "Todos Pagamentos / Tipos",
            "Apenas Saídas (Gastos)",
            "Apenas Entradas (Receitas)",
            "Cartão de Crédito",
            "Pix",
            "Débito",
            "Dinheiro"
        };
        ComboFiltroMetodo.SelectedIndex = 0;

        ComboEditFormaCofre.ItemsSource = new List<string> { "Pix", "Cartão de Crédito", "Débito", "Dinheiro" };

        _inicializando = false;
        CarregarRegistrosDoBanco();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void RecarregarCategoriasFiltroEEdicao()
    {
        var cats = _cofre.ObterCategorias();
        var listaFiltro = new List<string> { "Todas Categorias", "Receitas" };
        listaFiltro.AddRange(cats.Where(c => !c.Equals("Receitas", StringComparison.OrdinalIgnoreCase)));

        ComboFiltroCategoria.ItemsSource = listaFiltro;
        ComboFiltroCategoria.SelectedIndex = 0;

        var listaEdicao = new List<string>(cats);
        if (!listaEdicao.Contains("Receitas"))
            listaEdicao.Add("Receitas");
        ComboEditCatCofre.ItemsSource = listaEdicao;
    }

    private void CarregarRegistrosDoBanco()
    {
        using var conn = _cofre.CriarConexaoAberta();
        _todosRegistros = conn.Query<RegistroCofreItemViewModel>("SELECT * FROM Transacoes ORDER BY Data DESC, Id DESC").ToList();
        AplicarFiltrosNaLista();
    }

    private void Filtro_Changed(object sender, EventArgs e)
    {
        if (_inicializando) return;
        AplicarFiltrosNaLista();
    }

    private void BtnLimparFiltros_Click(object sender, RoutedEventArgs e)
    {
        _inicializando = true;
        TxtBuscaCofre.Clear();
        ComboFiltroMes.SelectedIndex = 0;
        ComboFiltroAno.SelectedIndex = 0;
        ComboFiltroCategoria.SelectedIndex = 0;
        ComboFiltroMetodo.SelectedIndex = 0;
        _inicializando = false;
        AplicarFiltrosNaLista();
    }

    private void AplicarFiltrosNaLista()
    {
        IEnumerable<RegistroCofreItemViewModel> query = _todosRegistros;

        string termo = (TxtBuscaCofre.Text ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(termo))
        {
            query = query.Where(x =>
                (x.Descricao ?? "").ToLowerInvariant().Contains(termo) ||
                (x.Categoria ?? "").ToLowerInvariant().Contains(termo) ||
                (x.TitularNome ?? "").ToLowerInvariant().Contains(termo) ||
                x.Valor.ToString("N2", new CultureInfo("pt-BR")).Contains(termo));
        }

        if (ComboFiltroMes.SelectedIndex > 0)
        {
            int mesAlvo = ComboFiltroMes.SelectedIndex;
            query = query.Where(x => x.Data.Month == mesAlvo);
        }

        string anoAlvo = ComboFiltroAno.SelectedItem?.ToString() ?? "Todos";
        if (anoAlvo != "Todos" && int.TryParse(anoAlvo, out int anoInt))
        {
            query = query.Where(x => x.Data.Year == anoInt);
        }

        string catAlvo = ComboFiltroCategoria.SelectedItem?.ToString() ?? "Todas Categorias";
        if (catAlvo != "Todas Categorias")
        {
            query = query.Where(x => (x.Categoria ?? "").Equals(catAlvo, StringComparison.OrdinalIgnoreCase));
        }

        string metodoAlvo = ComboFiltroMetodo.SelectedItem?.ToString() ?? "Todos Pagamentos / Tipos";
        if (metodoAlvo == "Apenas Saídas (Gastos)")
            query = query.Where(x => x.Tipo == TipoTransacao.Saida);
        else if (metodoAlvo == "Apenas Entradas (Receitas)")
            query = query.Where(x => x.Tipo == TipoTransacao.Entrada);
        else if (metodoAlvo != "Todos Pagamentos / Tipos")
            query = query.Where(x => (x.FormaPagamento ?? "Pix").Equals(metodoAlvo, StringComparison.OrdinalIgnoreCase));

        _registrosFiltrados = query.ToList();
        ListaTransacoesCofre.ItemsSource = _registrosFiltrados;

        decimal somaSaidas = _registrosFiltrados.Where(x => x.Tipo == TipoTransacao.Saida).Sum(x => x.Valor);
        decimal somaEntradas = _registrosFiltrados.Where(x => x.Tipo == TipoTransacao.Entrada).Sum(x => x.Valor);
        decimal maiorSaida = _registrosFiltrados.Where(x => x.Tipo == TipoTransacao.Saida).Select(x => x.Valor).DefaultIfEmpty(0m).Max();

        TxtResumoFiltroEsquerda.Text = $"EXIBINDO: {_registrosFiltrados.Count} REGISTRO(S) NO COFRE CRIPTOGRAFADO";
        TxtResumoFiltroDireita.Text = $"SAÍDAS: R$ {somaSaidas:N2} | ENTRADAS: R$ {somaEntradas:N2} | MAIOR GASTO: R$ {maiorSaida:N2}";
    }

    private void ListaTransacoesCofre_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaTransacoesCofre.SelectedItem is not RegistroCofreItemViewModel item)
        {
            _itemEmEdicao = null;
            return;
        }

        _itemEmEdicao = item;
        _formatandoCampo = true;
        try
        {
            TxtEditDescCofre.Text = item.Descricao;
            TxtEditValCofre.Text = item.Valor.ToString("N2", new CultureInfo("pt-BR"));
            TxtEditDataCofre.Text = item.Data.ToString("dd/MM/yyyy");
            ComboEditCatCofre.SelectedItem = item.Categoria;
            ComboEditFormaCofre.SelectedItem = string.IsNullOrWhiteSpace(item.FormaPagamento) ? "Pix" : item.FormaPagamento;
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void ApenasDigitos_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TxtEditValCofre_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo) return;
        _formatandoCampo = true;
        try
        {
            string digitos = Regex.Replace(TxtEditValCofre.Text ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(digitos) || !long.TryParse(digitos, out long centavos) || centavos == 0)
            {
                TxtEditValCofre.Text = string.Empty;
            }
            else
            {
                decimal v = centavos / 100m;
                TxtEditValCofre.Text = v.ToString("N2", new CultureInfo("pt-BR"));
                TxtEditValCofre.CaretIndex = TxtEditValCofre.Text.Length;
            }
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void TxtEditDataCofre_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo) return;
        _formatandoCampo = true;
        try
        {
            string d = Regex.Replace(TxtEditDataCofre.Text ?? "", @"[^\d]", "");
            if (d.Length > 8) d = d.Substring(0, 8);

            TxtEditDataCofre.Text = d.Length switch
            {
                <= 2 => d,
                <= 4 => $"{d.Substring(0, 2)}/{d.Substring(2)}",
                _ => $"{d.Substring(0, 2)}/{d.Substring(2, 2)}/{d.Substring(4)}"
            };
            TxtEditDataCofre.CaretIndex = TxtEditDataCofre.Text.Length;
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void BtnSalvarEdicaoCofre_Click(object sender, RoutedEventArgs e)
    {
        if (_itemEmEdicao == null)
        {
            JanelaMensagem.Exibir("Selecione um registro na lista acima para editar!", "Mini-Ordis // Edição", false, this);
            return;
        }

        string novaDesc = TxtEditDescCofre.Text.Trim();
        decimal.TryParse((TxtEditValCofre.Text ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal novoVal);

        if (string.IsNullOrWhiteSpace(novaDesc) || novoVal <= 0)
        {
            JanelaMensagem.Exibir("Preencha uma Descrição válida e um Valor maior que zero!", "Mini-Ordis // Edição", false, this);
            return;
        }

        if (!DateTime.TryParseExact(TxtEditDataCofre.Text.Trim(), "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime novaData))
            novaData = _itemEmEdicao.Data;

        string novaCat = ComboEditCatCofre.SelectedItem?.ToString() ?? _itemEmEdicao.Categoria;
        string novaForma = ComboEditFormaCofre.SelectedItem?.ToString() ?? _itemEmEdicao.FormaPagamento;

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            UPDATE Transacoes SET
                Descricao = @Desc,
                Valor = @Val,
                Data = @Data,
                Categoria = @Cat,
                FormaPagamento = @Forma
            WHERE Id = @Id",
            new
            {
                _itemEmEdicao.Id,
                Desc = novaDesc,
                Val = novoVal,
                Data = novaData.ToString("yyyy-MM-dd"),
                Cat = novaCat,
                Forma = novaForma
            });

        if (_itemEmEdicao.Tipo == TipoTransacao.Saida)
            _cofre.AprenderRegraEstabelecimento(novaDesc, novaCat);

        HouveAlteracao = true;
        CarregarRegistrosDoBanco();
        TxtStatusCofreModal.Text = $"✔ Registro '{novaDesc}' atualizado no Cofre e regra neural sincronizada!";
    }

    private void BtnExcluirSelecionados_Click(object sender, RoutedEventArgs e)
    {
        var selecionados = ListaTransacoesCofre.SelectedItems.OfType<RegistroCofreItemViewModel>().ToList();
        if (!selecionados.Any())
        {
            JanelaMensagem.Exibir("Selecione pelo menos um registro na lista para excluir!", "Mini-Ordis // Exclusão", false, this);
            return;
        }

        decimal somaExcluir = selecionados.Sum(x => x.Valor);
        string aviso = selecionados.Count == 1
            ? $"⚠ Confirmar exclusão do registro:\n\n• {selecionados[0].Descricao} (R$ {selecionados[0].Valor:N2})?"
            : $"⚠ Confirmar exclusão em lote de {selecionados.Count} registros selecionados (Total: R$ {somaExcluir:N2})?";

        if (!JanelaMensagem.Exibir(aviso, "CONFIRMAR EXCLUSÃO DO COFRE", confirmarSimNao: true, owner: this))
            return;

        using var conn = _cofre.CriarConexaoAberta();
        using var trans = conn.BeginTransaction();
        foreach (var item in selecionados)
        {
            conn.Execute("DELETE FROM Transacoes WHERE Id = @Id", new { item.Id }, trans);
        }
        trans.Commit();

        HouveAlteracao = true;
        CarregarRegistrosDoBanco();
        TxtStatusCofreModal.Text = $"✔ {selecionados.Count} registro(s) removido(s) do Cofre Criptografado!";
    }

    private void BtnExportarCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!_registrosFiltrados.Any())
        {
            JanelaMensagem.Exibir("Não há registros no filtro atual para exportar!", "Mini-Ordis // Exportação", false, this);
            return;
        }

        string pastaRelatorios = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CofreDados", "Relatorios");
        if (!Directory.Exists(pastaRelatorios))
            Directory.CreateDirectory(pastaRelatorios);

        string arquivoCsv = Path.Combine(pastaRelatorios, $"Relatorio_Ordis_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("ID;Data;Tipo;Descricao;Categoria;FormaPagamento;Valor");

        foreach (var r in _registrosFiltrados)
        {
            sb.AppendLine($"{r.Id};{r.Data:dd/MM/yyyy};{r.TipoBadge};{r.Descricao.Replace(";", ",")};{r.Categoria};{r.FormaPagamento};{r.Valor:N2}");
        }

        File.WriteAllText(arquivoCsv, sb.ToString(), Encoding.UTF8);
        JanelaMensagem.Exibir($"✔ Relatório com {_registrosFiltrados.Count} registro(s) exportado com sucesso em:\n\n{arquivoCsv}", "Mini-Ordis // Exportação Concluída", false, this);
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = HouveAlteracao;
        Close();
    }
}
