using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dapper;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public class EntradaCaixaViewModel : Transacao
{
    public string LinhaPrincipal
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TitularNome))
            {
                string cpfLimpo = (TitularCpf ?? "").StartsWith("SEM-CPF") ? "" : TitularCpf ?? "";
                string cpfInfo = !string.IsNullOrWhiteSpace(cpfLimpo) ? $" (CPF: {cpfLimpo})" : "";
                return $"👤 {TitularNome.ToUpper()}{cpfInfo}";
            }
            return $"⚡ {Descricao.ToUpper()}";
        }
    }

    public string ValorLiquidoTexto => $"+ R$ {Valor:N2}";

    public string LinhaSecundaria
    {
        get
        {
            string nat = string.IsNullOrWhiteSpace(NaturezaReceita) ? "Salário / Contracheque" : NaturezaReceita;
            string detalheBruto = (ValorBruto > 0 && ValorDescontos > 0)
                ? $" | Bruto: R$ {ValorBruto:N2} (-R$ {ValorDescontos:N2} retido)"
                : "";
            return $"[{Data:dd/MM/yyyy}] {Descricao} • [{nat}]{detalheBruto}";
        }
    }
}

public class OpcaoPerfilCombo
{
    public string TituloExibicao { get; set; } = string.Empty;
    public PerfilContracheque? Perfil { get; set; }
    public override string ToString() => TituloExibicao;
}

public partial class JanelaReceitasCaixa : Window
{
    private readonly CofreSqliteService _cofre;
    private readonly ContrachequeParserService _parser;
    private List<PerfilContracheque> _perfisMemoria = new();
    private readonly List<TextBox> _historicoEdicaoMoeda = new();
    private bool _formatandoCampo = false;
    private bool _processandoAutoComplete = false;
    private bool _ultimoTecladoFoiApagar = false;
    private bool _ultimoTecladoFoiApagarCpf = false;
    private bool _inicializando = true;
    public bool HouveAlteracao { get; private set; } = false;

    private static readonly string[] NomesMeses = {
        "01 - Jan", "02 - Fev", "03 - Mar", "04 - Abr", "05 - Mai", "06 - Jun",
        "07 - Jul", "08 - Ago", "09 - Set", "10 - Out", "11 - Nov", "12 - Dez"
    };

    public JanelaReceitasCaixa(CofreSqliteService cofre, int mesInicial, string anoInicial)
    {
        InitializeComponent();
        _cofre = cofre;
        _parser = new ContrachequeParserService(cofre);

        ComboMesCaixa.ItemsSource = NomesMeses;
        ComboMesCaixa.SelectedIndex = Math.Clamp(mesInicial - 1, 0, 11);

        var anos = new List<string> { "2025", "2026", "2027" };
        ComboAnoCaixa.ItemsSource = anos;
        ComboAnoCaixa.SelectedItem = anos.Contains(anoInicial) ? anoInicial : "2026";

        TxtDataEntrada.Text = DateTime.Now.ToString("dd/MM/yyyy");

        ComboNaturezaReceita.ItemsSource = new List<string>
        {
            "Salário / Contracheque",
            "Férias / 13º",
            "Renda Extra",
            "Reembolso / Divisão Pix"
        };
        ComboNaturezaReceita.SelectedIndex = 0;

        CarregarPerfisSalvosNaCombo();
        _inicializando = false;
        AtualizarPainelCaixaMes();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // 1. AUTO-COMPLETE NO NOME -> COMPLETA NOME E PREENCHE CPF
    private void TxtNomeTitular_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _ultimoTecladoFoiApagar = (e.Key == Key.Back || e.Key == Key.Delete);

        if (PopupAutoCompleteNome.IsOpen)
        {
            if (e.Key == Key.Down && ListaSugestoesNome.Items.Count > 0)
            {
                ListaSugestoesNome.SelectedIndex = Math.Min(ListaSugestoesNome.SelectedIndex + 1, ListaSugestoesNome.Items.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ListaSugestoesNome.Items.Count > 0)
            {
                ListaSugestoesNome.SelectedIndex = Math.Max(ListaSugestoesNome.SelectedIndex - 1, 0);
                e.Handled = true;
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Tab) && ListaSugestoesNome.SelectedItem is OpcaoPerfilCombo sel && sel.Perfil != null)
            {
                AplicarPerfilNoFormulario(sel.Perfil);
                PopupAutoCompleteNome.IsOpen = false;
                if (e.Key == Key.Enter) e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                PopupAutoCompleteNome.IsOpen = false;
                e.Handled = true;
            }
        }
    }

    private void TxtNomeTitular_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_processandoAutoComplete || _inicializando) return;

        string digitado = TxtNomeTitular.Text;
        if (string.IsNullOrWhiteSpace(digitado))
        {
            PopupAutoCompleteNome.IsOpen = false;
            return;
        }

        var correspondencias = _perfisMemoria
            .Where(p => p.NomeTitular.Contains(digitado.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(p => new OpcaoPerfilCombo
            {
                Perfil = p,
                TituloExibicao = $"👤 {p.NomeTitular} {(string.IsNullOrWhiteSpace(p.CpfTitular) || p.CpfTitular.StartsWith("SEM-CPF") ? "(Sem CPF vinculado)" : $"— CPF: {p.CpfTitular}")}"
            })
            .ToList();

        if (correspondencias.Any())
        {
            ListaSugestoesNome.ItemsSource = correspondencias;
            ListaSugestoesNome.SelectedIndex = 0;
            PopupAutoCompleteNome.IsOpen = true;

            var matchPrefixo = _perfisMemoria.FirstOrDefault(p => p.NomeTitular.StartsWith(digitado, StringComparison.OrdinalIgnoreCase));
            if (matchPrefixo != null && !_ultimoTecladoFoiApagar && digitado.Length >= 2)
            {
                _processandoAutoComplete = true;
                try
                {
                    int posCursor = digitado.Length;
                    TxtNomeTitular.Text = matchPrefixo.NomeTitular;
                    TxtNomeTitular.Select(posCursor, matchPrefixo.NomeTitular.Length - posCursor);

                    PreencherDadosDoPerfilEncontrado(matchPrefixo, atualizarCpf: true, atualizarNome: false);
                }
                finally
                {
                    _processandoAutoComplete = false;
                }
            }
        }
        else
        {
            PopupAutoCompleteNome.IsOpen = false;
        }
    }

    private void ListaSugestoesNome_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ListaSugestoesNome.SelectedItem is OpcaoPerfilCombo sel && sel.Perfil != null)
        {
            AplicarPerfilNoFormulario(sel.Perfil);
            PopupAutoCompleteNome.IsOpen = false;
        }
    }

    // 2. AUTO-COMPLETE NO CPF -> COMPLETA CPF E PREENCHE O NOME ATRELADO A ELE!
    private void TxtCpfTitular_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _ultimoTecladoFoiApagarCpf = (e.Key == Key.Back || e.Key == Key.Delete);

        if (PopupAutoCompleteCpf.IsOpen)
        {
            if (e.Key == Key.Down && ListaSugestoesCpf.Items.Count > 0)
            {
                ListaSugestoesCpf.SelectedIndex = Math.Min(ListaSugestoesCpf.SelectedIndex + 1, ListaSugestoesCpf.Items.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ListaSugestoesCpf.Items.Count > 0)
            {
                ListaSugestoesCpf.SelectedIndex = Math.Max(ListaSugestoesCpf.SelectedIndex - 1, 0);
                e.Handled = true;
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Tab) && ListaSugestoesCpf.SelectedItem is OpcaoPerfilCombo sel && sel.Perfil != null)
            {
                AplicarPerfilNoFormulario(sel.Perfil);
                PopupAutoCompleteCpf.IsOpen = false;
                if (e.Key == Key.Enter) e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                PopupAutoCompleteCpf.IsOpen = false;
                e.Handled = true;
            }
        }
    }

    private void TxtCpfTitular_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo || _processandoAutoComplete || _inicializando) return;

        _formatandoCampo = true;
        try
        {
            string cpfFormatado = ContrachequeParserService.FormatarCpf(TxtCpfTitular.Text);
            TxtCpfTitular.Text = cpfFormatado;
            TxtCpfTitular.CaretIndex = TxtCpfTitular.Text.Length;

            string digitosDigitados = ContrachequeParserService.LimparDigitos(cpfFormatado);
            if (string.IsNullOrWhiteSpace(digitosDigitados))
            {
                PopupAutoCompleteCpf.IsOpen = false;
                return;
            }

            // Procura nos perfis salvos todos os CPFs validos que começam ou contêm esses digitos
            var correspondenciasCpf = _perfisMemoria
                .Where(p => !string.IsNullOrWhiteSpace(p.CpfTitular) &&
                            !p.CpfTitular.StartsWith("SEM-CPF") &&
                            ContrachequeParserService.LimparDigitos(p.CpfTitular).Contains(digitosDigitados))
                .Select(p => new OpcaoPerfilCombo
                {
                    Perfil = p,
                    TituloExibicao = $"🪪 {p.CpfTitular} — {p.NomeTitular}"
                })
                .ToList();

            if (correspondenciasCpf.Any())
            {
                ListaSugestoesCpf.ItemsSource = correspondenciasCpf;
                ListaSugestoesCpf.SelectedIndex = 0;
                PopupAutoCompleteCpf.IsOpen = true;

                // Se o CPF salvo começa exatamente com os digitos digitados, ja completa o CPF e preenche o Nome atrelado!
                var matchCpfPrefixo = _perfisMemoria.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.CpfTitular) &&
                    !p.CpfTitular.StartsWith("SEM-CPF") &&
                    ContrachequeParserService.LimparDigitos(p.CpfTitular).StartsWith(digitosDigitados));

                if (matchCpfPrefixo != null && !_ultimoTecladoFoiApagarCpf && digitosDigitados.Length >= 2)
                {
                    _processandoAutoComplete = true;
                    try
                    {
                        string cpfCompleto = ContrachequeParserService.FormatarCpf(matchCpfPrefixo.CpfTitular);
                        int posCursor = cpfFormatado.Length;
                        TxtCpfTitular.Text = cpfCompleto;
                        if (cpfCompleto.Length > posCursor)
                            TxtCpfTitular.Select(posCursor, cpfCompleto.Length - posCursor);

                        PreencherDadosDoPerfilEncontrado(matchCpfPrefixo, atualizarCpf: false, atualizarNome: true);
                    }
                    finally
                    {
                        _processandoAutoComplete = false;
                    }
                }
            }
            else
            {
                PopupAutoCompleteCpf.IsOpen = false;
            }
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void ListaSugestoesCpf_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ListaSugestoesCpf.SelectedItem is OpcaoPerfilCombo sel && sel.Perfil != null)
        {
            AplicarPerfilNoFormulario(sel.Perfil);
            PopupAutoCompleteCpf.IsOpen = false;
        }
    }

    private void AplicarPerfilNoFormulario(PerfilContracheque p)
    {
        _processandoAutoComplete = true;
        _formatandoCampo = true;
        try
        {
            TxtNomeTitular.Text = p.NomeTitular;
            TxtNomeTitular.CaretIndex = TxtNomeTitular.Text.Length;

            string cpfSalvo = (p.CpfTitular ?? "").StartsWith("SEM-CPF") ? "" : (p.CpfTitular ?? "");
            TxtCpfTitular.Text = cpfSalvo;
            TxtCpfTitular.CaretIndex = TxtCpfTitular.Text.Length;

            PreencherDadosDoPerfilEncontrado(p, atualizarCpf: false, atualizarNome: false);
        }
        finally
        {
            _formatandoCampo = false;
            _processandoAutoComplete = false;
        }
    }

    private void PreencherDadosDoPerfilEncontrado(PerfilContracheque p, bool atualizarCpf, bool atualizarNome)
    {
        _formatandoCampo = true;
        _processandoAutoComplete = true;
        try
        {
            _historicoEdicaoMoeda.Clear();
            if (atualizarNome && !string.IsNullOrWhiteSpace(p.NomeTitular))
                TxtNomeTitular.Text = p.NomeTitular;

            if (atualizarCpf)
            {
                string cpfSalvo = (p.CpfTitular ?? "").StartsWith("SEM-CPF") ? "" : (p.CpfTitular ?? "");
                if (!string.IsNullOrWhiteSpace(cpfSalvo))
                    TxtCpfTitular.Text = cpfSalvo;
            }

            string nat = ComboNaturezaReceita.SelectedItem?.ToString() ?? "Salário / Contracheque";
            if (nat == "Salário / Contracheque")
            {
                if (p.ValorBrutoPadrao > 0)
                    TxtValorBruto.Text = p.ValorBrutoPadrao.ToString("N2", new CultureInfo("pt-BR"));
                if (p.ValorDescontosPadrao > 0)
                    TxtValorDescontos.Text = p.ValorDescontosPadrao.ToString("N2", new CultureInfo("pt-BR"));
                if (p.ValorLiquidoPadrao > 0)
                    TxtValorLiquido.Text = p.ValorLiquidoPadrao.ToString("N2", new CultureInfo("pt-BR"));
                if (!string.IsNullOrWhiteSpace(p.DescricaoPadrao))
                    TxtDescricaoReceita.Text = p.DescricaoPadrao;
            }
        }
        finally
        {
            _formatandoCampo = false;
            _processandoAutoComplete = false;
        }
    }

    private void ComboNaturezaReceita_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PainelBruto == null || PainelDescontos == null) return;

        string nat = ComboNaturezaReceita.SelectedItem?.ToString() ?? "Salário / Contracheque";
        bool ehReembolsoOuExtra = nat == "Reembolso / Divisão Pix" || nat == "Renda Extra";

        if (ehReembolsoOuExtra)
        {
            PainelBruto.Visibility = Visibility.Collapsed;
            PainelDescontos.Visibility = Visibility.Collapsed;
            ColBruto.Width = new GridLength(0);
            ColDescontos.Width = new GridLength(0);
            LblValorLiquido.Text = nat == "Reembolso / Divisão Pix"
                ? "VALOR DO REEMBOLSO PIX (R$):"
                : "VALOR DA RENDA EXTRA (R$):";
            LblNomeTitular.Text = "Nome / Origem (Auto-Complete):";
            LblCpfTitular.Text = "CPF (Auto-Complete Opcional):";

            if (TxtDescricaoReceita.Text.Contains("Contracheque"))
                TxtDescricaoReceita.Text = nat;
        }
        else
        {
            PainelBruto.Visibility = Visibility.Visible;
            PainelDescontos.Visibility = Visibility.Visible;
            ColBruto.Width = new GridLength(1, GridUnitType.Star);
            ColDescontos.Width = new GridLength(1, GridUnitType.Star);
            LblValorLiquido.Text = "VALOR LÍQUIDO REAL (R$):";
            LblNomeTitular.Text = "Nome do Titular (Auto-Complete ⇄ CPF):";
            LblCpfTitular.Text = "CPF (Auto-Complete ⇄ Nome):";

            if (TxtDescricaoReceita.Text == "Reembolso / Divisão Pix" || TxtDescricaoReceita.Text == "Renda Extra")
                TxtDescricaoReceita.Text = "Contracheque Mensal";
        }
    }

    private void CarregarPerfisSalvosNaCombo()
    {
        _perfisMemoria = _cofre.ObterPerfisContracheque();
        var opcoes = new List<OpcaoPerfilCombo>
        {
            new OpcaoPerfilCombo { TituloExibicao = "+ Digitar no Auto-Complete abaixo (Nome ou CPF)", Perfil = null }
        };

        foreach (var p in _perfisMemoria)
        {
            string cpfExib = string.IsNullOrWhiteSpace(p.CpfTitular) || p.CpfTitular.StartsWith("SEM-CPF")
                ? "Sem CPF ainda"
                : $"CPF: {p.CpfTitular}";

            opcoes.Add(new OpcaoPerfilCombo
            {
                TituloExibicao = $"{p.NomeTitular} ({cpfExib}) — Líquido: R$ {p.ValorLiquidoPadrao:N2}",
                Perfil = p
            });
        }

        ComboPerfisSalvos.ItemsSource = opcoes;
        ComboPerfisSalvos.SelectedIndex = 0;
    }

    private void ComboPerfisSalvos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboPerfisSalvos.SelectedItem is not OpcaoPerfilCombo opcao || opcao.Perfil == null)
            return;

        AplicarPerfilNoFormulario(opcao.Perfil);
    }

    private void FiltroMesAno_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_inicializando) return;
        AtualizarPainelCaixaMes();
    }

    private string ObterAnoMesSelecionado(out int ano, out int mes)
    {
        mes = ComboMesCaixa.SelectedIndex + 1;
        int.TryParse(ComboAnoCaixa.SelectedItem?.ToString() ?? "2026", out ano);
        if (ano < 2000) ano = DateTime.Now.Year;
        return $"{ano}-{mes:D2}";
    }

    private void AtualizarPainelCaixaMes()
    {
        string ym = ObterAnoMesSelecionado(out int ano, out int mes);
        using var conn = _cofre.CriarConexaoAberta();

        var entradas = conn.Query<EntradaCaixaViewModel>(@"
            SELECT * FROM Transacoes 
            WHERE Tipo = 1 AND substr(Data, 1, 7) = @Ym 
            ORDER BY Id DESC", new { Ym = ym }).ToList();

        ListaEntradasMes.ItemsSource = entradas;

        var balanco = _cofre.CalcularBalancoMes(ym);
        decimal totalDescontosRetidos = entradas.Sum(x => x.ValorDescontos);
        decimal metaEscudo10 = Math.Round(balanco.RendaLiquidaReal * 0.10m, 2);
        decimal saldoLivre = balanco.RendaLiquidaReal - balanco.GastosLiquidos - metaEscudo10;

        int diasNoMes = DateTime.DaysInMonth(ano, mes);
        int diasRestantes = (ano == DateTime.Now.Year && mes == DateTime.Now.Month)
            ? Math.Max(1, diasNoMes - DateTime.Now.Day + 1)
            : diasNoMes;

        decimal poderDiario = saldoLivre > 0 ? saldoLivre / diasRestantes : 0m;

        TxtResumoCaixaTopo.Text =
            $"CAIXA LÍQUIDO: R$ {balanco.RendaLiquidaReal:N2} | GASTOS LÍQUIDOS: R$ {balanco.GastosLiquidos:N2} | ESCUDO (10%): R$ {metaEscudo10:N2}";

        if (balanco.RendaLiquidaReal <= 0)
        {
            TxtPoderDeFogoDiario.Text = $"ℹ Nenhuma renda lançada em {ym} (Reembolsos abatidos no mês: R$ {balanco.ReembolsosAbatidos:N2})";
            TxtPoderDeFogoDiario.Foreground = new SolidColorBrush(Color.FromRgb(255, 184, 0));
        }
        else if (saldoLivre >= 0)
        {
            TxtPoderDeFogoDiario.Text =
                $"⚡ SALDO LIVRE APÓS ESCUDO: R$ {saldoLivre:N2} // PODER DE FOGO DIÁRIO: R$ {poderDiario:N2} / dia ({diasRestantes} dias)";
            TxtPoderDeFogoDiario.Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255));
        }
        else
        {
            TxtPoderDeFogoDiario.Text =
                $"🚨 ALERTA VERMELHO: Gastos ultrapassaram a margem do Escudo em R$ {Math.Abs(saldoLivre):N2}!";
            TxtPoderDeFogoDiario.Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 85));
        }

        string extraReembolso = balanco.ReembolsosAbatidos > 0
            ? $" | Reembolsos Pix abatidos dos gastos: R$ {balanco.ReembolsosAbatidos:N2}"
            : "";

        TxtDetalheImpostosMes.Text =
            $"Retenções na fonte nos contracheques do mês (INSS/IRRF/Adiantamentos): R$ {totalDescontosRetidos:N2}{extraReembolso}";
    }

    private void ApenasDigitos_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TxtDataEntrada_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo) return;
        _formatandoCampo = true;
        try
        {
            string d = Regex.Replace(TxtDataEntrada.Text ?? "", @"[^\d]", "");
            if (d.Length > 8) d = d.Substring(0, 8);

            TxtDataEntrada.Text = d.Length switch
            {
                <= 2 => d,
                <= 4 => $"{d.Substring(0, 2)}/{d.Substring(2)}",
                _ => $"{d.Substring(0, 2)}/{d.Substring(2, 2)}/{d.Substring(4)}"
            };
            TxtDataEntrada.CaretIndex = TxtDataEntrada.Text.Length;
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void CampoMoeda_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo || sender is not TextBox tb) return;
        _formatandoCampo = true;
        try
        {
            string digitos = Regex.Replace(tb.Text ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(digitos) || !long.TryParse(digitos, out long centavos) || centavos == 0)
            {
                tb.Text = string.Empty;
            }
            else
            {
                decimal v = centavos / 100m;
                tb.Text = v.ToString("N2", new CultureInfo("pt-BR"));
                tb.CaretIndex = tb.Text.Length;
            }

            RecalcularTrianguloMoeda(tb);
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void RecalcularTrianguloMoeda(TextBox tbAtual)
    {
        _historicoEdicaoMoeda.Remove(tbAtual);
        _historicoEdicaoMoeda.Add(tbAtual);
        if (_historicoEdicaoMoeda.Count > 2)
            _historicoEdicaoMoeda.RemoveAt(0);

        decimal bruto = LerDecimal(TxtValorBruto.Text);
        decimal desc = LerDecimal(TxtValorDescontos.Text);
        decimal liq = LerDecimal(TxtValorLiquido.Text);

        var cultura = new CultureInfo("pt-BR");
        bool temBrutoLiq = _historicoEdicaoMoeda.Contains(TxtValorBruto) && _historicoEdicaoMoeda.Contains(TxtValorLiquido);
        bool temLiqDesc = _historicoEdicaoMoeda.Contains(TxtValorLiquido) && _historicoEdicaoMoeda.Contains(TxtValorDescontos);
        bool temBrutoDesc = _historicoEdicaoMoeda.Contains(TxtValorBruto) && _historicoEdicaoMoeda.Contains(TxtValorDescontos);

        if (temLiqDesc || (tbAtual == TxtValorLiquido && desc > 0 && !temBrutoLiq))
        {
            if (liq > 0 || desc > 0)
                TxtValorBruto.Text = (liq + desc).ToString("N2", cultura);
        }
        else if (temBrutoDesc || (tbAtual == TxtValorBruto && desc > 0 && !temBrutoLiq))
        {
            if (bruto >= desc && bruto > 0)
                TxtValorLiquido.Text = (bruto - desc).ToString("N2", cultura);
        }
        else if (temBrutoLiq)
        {
            if (bruto >= liq && bruto > 0)
                TxtValorDescontos.Text = (bruto - liq).ToString("N2", cultura);
        }
        else if (tbAtual == TxtValorDescontos)
        {
            if (liq > 0 && bruto <= 0)
                TxtValorBruto.Text = (liq + desc).ToString("N2", cultura);
            else if (bruto >= desc && bruto > 0)
                TxtValorLiquido.Text = (bruto - desc).ToString("N2", cultura);
        }
    }

    private decimal LerDecimal(string? texto)
    {
        decimal.TryParse((texto ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v);
        return v;
    }

    private void BtnImportarHolerite_Click(object sender, RoutedEventArgs e)
    {
        var explorador = new JanelaExploradorArquivos { Owner = this };
        if (explorador.ShowDialog() == true && explorador.ArquivosSelecionados.Any())
        {
            string arquivo = explorador.ArquivosSelecionados[0];
            var (extraido, dataDoc) = _parser.AnalisarArquivoCompleto(arquivo);

            _processandoAutoComplete = true;
            _formatandoCampo = true;
            try
            {
                _historicoEdicaoMoeda.Clear();
                if (!string.IsNullOrWhiteSpace(extraido.NomeTitular))
                    TxtNomeTitular.Text = extraido.NomeTitular;

                if (!string.IsNullOrWhiteSpace(extraido.CpfTitular))
                    TxtCpfTitular.Text = extraido.CpfTitular;

                TxtDataEntrada.Text = dataDoc.ToString("dd/MM/yyyy");

                _inicializando = true;
                ComboMesCaixa.SelectedIndex = dataDoc.Month - 1;
                string anoDocStr = dataDoc.Year.ToString();
                if (ComboAnoCaixa.ItemsSource is IEnumerable<string> anos && anos.Contains(anoDocStr))
                    ComboAnoCaixa.SelectedItem = anoDocStr;
                _inicializando = false;

                TxtValorBruto.Text = extraido.ValorBruto > 0 ? extraido.ValorBruto.ToString("N2", new CultureInfo("pt-BR")) : "";
                TxtValorDescontos.Text = extraido.ValorDescontos > 0 ? extraido.ValorDescontos.ToString("N2", new CultureInfo("pt-BR")) : "";
                TxtValorLiquido.Text = extraido.ValorLiquido > 0 ? extraido.ValorLiquido.ToString("N2", new CultureInfo("pt-BR")) : "";
            }
            finally
            {
                _formatandoCampo = false;
                _processandoAutoComplete = false;
            }

            TxtDescricaoReceita.Text = extraido.DescricaoSugerida;
            AtualizarPainelCaixaMes();
            TxtStatusRodape.Text = $"✔ Arquivo '{Path.GetFileName(arquivo)}' processado para {dataDoc:MM/yyyy}!";
        }
    }

    private void BtnSalvarEntrada_Click(object sender, RoutedEventArgs e)
    {
        string natureza = ComboNaturezaReceita.SelectedItem?.ToString() ?? "Salário / Contracheque";
        bool ehReembolsoOuExtra = natureza == "Reembolso / Divisão Pix" || natureza == "Renda Extra";

        string nome = TxtNomeTitular.Text.Trim();
        string cpf = TxtCpfTitular.Text.Trim();
        string desc = string.IsNullOrWhiteSpace(TxtDescricaoReceita.Text) ? natureza : TxtDescricaoReceita.Text.Trim();

        decimal bruto = ehReembolsoOuExtra ? 0m : LerDecimal(TxtValorBruto.Text);
        decimal descontos = ehReembolsoOuExtra ? 0m : LerDecimal(TxtValorDescontos.Text);
        decimal liquido = LerDecimal(TxtValorLiquido.Text);

        if (liquido <= 0 && bruto > 0)
            liquido = Math.Max(0m, bruto - descontos);
        if (bruto <= 0 && liquido > 0 && !ehReembolsoOuExtra)
            bruto = liquido + descontos;

        if (liquido <= 0)
        {
            JanelaMensagem.Exibir("Digite um valor maior que zero no campo de Valor Líquido / Entrada para salvar!", "Mini-Ordis // Valor Necessário", false, this);
            return;
        }

        // Se digitou apenas o CPF, puxa o Nome vinculado do banco!
        if (string.IsNullOrWhiteSpace(nome) && !string.IsNullOrWhiteSpace(cpf))
        {
            var perfilPorCpf = _perfisMemoria.FirstOrDefault(p => ContrachequeParserService.LimparDigitos(p.CpfTitular) == ContrachequeParserService.LimparDigitos(cpf));
            if (perfilPorCpf != null)
                nome = perfilPorCpf.NomeTitular;
        }

        if (string.IsNullOrWhiteSpace(nome))
            nome = ehReembolsoOuExtra ? natureza : "Titular Principal";

        // Se digitou apenas o Nome, puxa o CPF vinculado do banco!
        if (string.IsNullOrWhiteSpace(cpf))
        {
            var perfilSalvo = _perfisMemoria.FirstOrDefault(p => p.NomeTitular.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (perfilSalvo != null && !perfilSalvo.CpfTitular.StartsWith("SEM-CPF"))
                cpf = perfilSalvo.CpfTitular;
        }

        if (!DateTime.TryParseExact(TxtDataEntrada.Text.Trim(), "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dataFinal))
        {
            ObterAnoMesSelecionado(out int anoSel, out int mesSel);
            int diaPadrao = (anoSel == DateTime.Now.Year && mesSel == DateTime.Now.Month) ? DateTime.Now.Day : 5;
            dataFinal = new DateTime(anoSel, mesSel, diaPadrao);
        }
        else
        {
            _inicializando = true;
            ComboMesCaixa.SelectedIndex = dataFinal.Month - 1;
            string anoStr = dataFinal.Year.ToString();
            if (ComboAnoCaixa.ItemsSource is IEnumerable<string> anos && anos.Contains(anoStr))
                ComboAnoCaixa.SelectedItem = anoStr;
            _inicializando = false;
        }

        string dataGravar = dataFinal.ToString("yyyy-MM-dd");

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            INSERT INTO Transacoes 
            (Descricao, Valor, ValorBruto, ValorDescontos, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo, TitularNome, TitularCpf, NaturezaReceita)
            VALUES (@Desc, @Val, @Bruto, @DescRetido, @Data, 1, 'Receitas', 'Pix', '', @Origem, @Nome, @Cpf, @Nat);",
            new
            {
                Desc = desc,
                Val = liquido,
                Bruto = bruto,
                DescRetido = descontos,
                Data = dataGravar,
                Origem = ehReembolsoOuExtra ? "Reembolso/Extra" : "Contracheque",
                Nome = nome,
                Cpf = cpf,
                Nat = natureza
            });

        if (!ehReembolsoOuExtra || !string.IsNullOrWhiteSpace(cpf))
        {
            _cofre.SalvarPerfilContracheque(new PerfilContracheque
            {
                NomeTitular = nome,
                CpfTitular = cpf,
                ValorBrutoPadrao = bruto,
                ValorDescontosPadrao = descontos,
                ValorLiquidoPadrao = liquido,
                DescricaoPadrao = desc
            });
            CarregarPerfisSalvosNaCombo();
        }

        _formatandoCampo = true;
        _historicoEdicaoMoeda.Clear();
        TxtValorBruto.Clear();
        TxtValorDescontos.Clear();
        TxtValorLiquido.Clear();
        _formatandoCampo = false;

        PopupAutoCompleteNome.IsOpen = false;
        PopupAutoCompleteCpf.IsOpen = false;
        HouveAlteracao = true;
        AtualizarPainelCaixaMes();
        TxtStatusRodape.Text = $"✔ '{desc}' de {nome} (R$ {liquido:N2}) salvo e vínculo Nome ⇄ CPF memorizado!";
    }

    private void ListaEntradasMes_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AbrirModalEdicaoSelecionada();
    private void BtnEditarEntrada_Click(object sender, RoutedEventArgs e) => AbrirModalEdicaoSelecionada();

    private void AbrirModalEdicaoSelecionada()
    {
        if (ListaEntradasMes.SelectedItem is not EntradaCaixaViewModel item)
        {
            JanelaMensagem.Exibir("Selecione uma entrada na lista à direita (ou dê 2 cliques sobre ela) para editar!", "Mini-Ordis // Editar Entrada", false, this);
            return;
        }

        var modal = new JanelaEditarEntrada(_cofre, item) { Owner = this };
        if (modal.ShowDialog() == true)
        {
            HouveAlteracao = true;
            CarregarPerfisSalvosNaCombo();
            AtualizarPainelCaixaMes();
            TxtStatusRodape.Text = $"✔ Registro '{item.Descricao}' atualizado com sucesso!";
        }
    }

    private void BtnClonarSalariosBase_Click(object sender, RoutedEventArgs e)
    {
        var perfis = _cofre.ObterPerfisContracheque();
        if (!perfis.Any())
        {
            JanelaMensagem.Exibir(
                "Ainda não há titulares memorizados no Cofre!\n\nSalve os seus 2 contracheques uma primeira vez usando o botão verde acima. Depois disso, este botão lançará ambos com 1 clique!",
                "Mini-Ordis // Clonador de Holerites",
                false,
                this);
            return;
        }

        string ym = ObterAnoMesSelecionado(out int ano, out int mes);
        string dataGravar = $"{ano}-{mes:D2}-05";

        using var conn = _cofre.CriarConexaoAberta();
        int injetados = 0;

        foreach (var p in perfis)
        {
            int jaExiste = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Transacoes 
                WHERE Tipo = 1 AND substr(Data, 1, 7) = @Ym AND LOWER(TitularNome) = LOWER(@Nome) AND NaturezaReceita = 'Salário / Contracheque'",
                new { Ym = ym, Nome = p.NomeTitular });

            if (jaExiste > 0) continue;

            conn.Execute(@"
                INSERT INTO Transacoes 
                (Descricao, Valor, ValorBruto, ValorDescontos, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo, TitularNome, TitularCpf, NaturezaReceita)
                VALUES (@Desc, @Val, @Bruto, @DescRetido, @Data, 1, 'Receitas', 'Pix', '', 'Clone 1-Clique', @Nome, @Cpf, 'Salário / Contracheque');",
                new
                {
                    Desc = p.DescricaoPadrao,
                    Val = p.ValorLiquidoPadrao,
                    Bruto = p.ValorBrutoPadrao,
                    DescRetido = p.ValorDescontosPadrao,
                    Data = dataGravar,
                    Nome = p.NomeTitular,
                    Cpf = p.CpfTitular.StartsWith("SEM-CPF") ? "" : p.CpfTitular
                });
            injetados++;
        }

        if (injetados > 0)
        {
            HouveAlteracao = true;
            AtualizarPainelCaixaMes();
            JanelaMensagem.Exibir(
                $"✔ {injetados} contracheque(s) salvo(s) foram injetados automaticamente no caixa de {ym}!",
                "Mini-Ordis // Injeção Rápida Concluída",
                false,
                this);
        }
        else
        {
            JanelaMensagem.Exibir(
                $"Todos os titulares cadastrados ({perfis.Count}) já possuem contracheque lançado no mês {ym}!",
                "Mini-Ordis // Anti-Duplicidade",
                false,
                this);
        }
    }

    private void BtnExcluirEntrada_Click(object sender, RoutedEventArgs e)
    {
        if (ListaEntradasMes.SelectedItem is not EntradaCaixaViewModel item)
        {
            JanelaMensagem.Exibir("Selecione uma entrada na lista à direita para remover!", "Mini-Ordis", false, this);
            return;
        }

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute("DELETE FROM Transacoes WHERE Id = @Id", new { item.Id });
        HouveAlteracao = true;
        AtualizarPainelCaixaMes();
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = HouveAlteracao;
        Close();
    }
}
