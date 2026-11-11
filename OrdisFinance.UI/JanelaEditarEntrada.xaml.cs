using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dapper;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public partial class JanelaEditarEntrada : Window
{
    private readonly CofreSqliteService _cofre;
    private readonly Transacao _transacaoOriginal;
    private readonly List<TextBox> _historicoEdicaoMoeda = new();
    private bool _formatandoCampo = false;

    public JanelaEditarEntrada(CofreSqliteService cofre, Transacao transacao)
    {
        InitializeComponent();
        _cofre = cofre;
        _transacaoOriginal = transacao;

        ComboEditNatureza.ItemsSource = new List<string>
        {
            "Salário / Contracheque",
            "Férias / 13º",
            "Renda Extra",
            "Reembolso / Divisão Pix"
        };

        PreencherDadosIniciais();
    }

    private void PreencherDadosIniciais()
    {
        _formatandoCampo = true;
        try
        {
            TxtEditNome.Text = _transacaoOriginal.TitularNome;
            TxtEditCpf.Text = (_transacaoOriginal.TitularCpf ?? "").StartsWith("SEM-CPF") ? "" : _transacaoOriginal.TitularCpf;
            TxtEditDescricao.Text = _transacaoOriginal.Descricao;
            TxtEditData.Text = _transacaoOriginal.Data.ToString("dd/MM/yyyy");

            string nat = string.IsNullOrWhiteSpace(_transacaoOriginal.NaturezaReceita) ? "Salário / Contracheque" : _transacaoOriginal.NaturezaReceita;
            ComboEditNatureza.SelectedItem = nat;

            TxtEditBruto.Text = _transacaoOriginal.ValorBruto > 0 ? _transacaoOriginal.ValorBruto.ToString("N2", new CultureInfo("pt-BR")) : "";
            TxtEditDescontos.Text = _transacaoOriginal.ValorDescontos > 0 ? _transacaoOriginal.ValorDescontos.ToString("N2", new CultureInfo("pt-BR")) : "";
            TxtEditLiquido.Text = _transacaoOriginal.Valor > 0 ? _transacaoOriginal.Valor.ToString("N2", new CultureInfo("pt-BR")) : "";
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ComboEditNatureza_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PainelEditBruto == null || PainelEditDescontos == null) return;

        string nat = ComboEditNatureza.SelectedItem?.ToString() ?? "Salário / Contracheque";
        bool ehReembolsoOuExtra = nat == "Reembolso / Divisão Pix" || nat == "Renda Extra";

        if (ehReembolsoOuExtra)
        {
            PainelEditBruto.Visibility = Visibility.Collapsed;
            PainelEditDescontos.Visibility = Visibility.Collapsed;
            ColEditBruto.Width = new GridLength(0);
            ColEditDescontos.Width = new GridLength(0);
            LblEditLiquido.Text = nat == "Reembolso / Divisão Pix" ? "VALOR DO REEMBOLSO (R$):" : "VALOR DA RENDA EXTRA (R$):";
        }
        else
        {
            PainelEditBruto.Visibility = Visibility.Visible;
            PainelEditDescontos.Visibility = Visibility.Visible;
            ColEditBruto.Width = new GridLength(1, GridUnitType.Star);
            ColEditDescontos.Width = new GridLength(1, GridUnitType.Star);
            LblEditLiquido.Text = "VALOR LÍQUIDO REAL (R$):";
        }
    }

    private void ApenasDigitos_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TxtEditCpf_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo) return;
        _formatandoCampo = true;
        try
        {
            TxtEditCpf.Text = ContrachequeParserService.FormatarCpf(TxtEditCpf.Text);
            TxtEditCpf.CaretIndex = TxtEditCpf.Text.Length;
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void TxtEditData_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoCampo) return;
        _formatandoCampo = true;
        try
        {
            string d = Regex.Replace(TxtEditData.Text ?? "", @"[^\d]", "");
            if (d.Length > 8) d = d.Substring(0, 8);

            TxtEditData.Text = d.Length switch
            {
                <= 2 => d,
                <= 4 => $"{d.Substring(0, 2)}/{d.Substring(2)}",
                _ => $"{d.Substring(0, 2)}/{d.Substring(2, 2)}/{d.Substring(4)}"
            };
            TxtEditData.CaretIndex = TxtEditData.Text.Length;
        }
        finally
        {
            _formatandoCampo = false;
        }
    }

    private void CampoMoedaEdit_TextChanged(object sender, TextChangedEventArgs e)
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

        decimal bruto = LerDecimal(TxtEditBruto.Text);
        decimal desc = LerDecimal(TxtEditDescontos.Text);
        decimal liq = LerDecimal(TxtEditLiquido.Text);

        var cultura = new CultureInfo("pt-BR");
        bool temBrutoLiq = _historicoEdicaoMoeda.Contains(TxtEditBruto) && _historicoEdicaoMoeda.Contains(TxtEditLiquido);
        bool temLiqDesc = _historicoEdicaoMoeda.Contains(TxtEditLiquido) && _historicoEdicaoMoeda.Contains(TxtEditDescontos);
        bool temBrutoDesc = _historicoEdicaoMoeda.Contains(TxtEditBruto) && _historicoEdicaoMoeda.Contains(TxtEditDescontos);

        if (temLiqDesc || (tbAtual == TxtEditLiquido && desc > 0 && !temBrutoLiq))
        {
            // Liquido + Desconto -> Calcula Bruto!
            if (liq > 0 || desc > 0)
                TxtEditBruto.Text = (liq + desc).ToString("N2", cultura);
        }
        else if (temBrutoDesc || (tbAtual == TxtEditBruto && desc > 0 && !temBrutoLiq))
        {
            // Bruto - Desconto -> Calcula Liquido!
            if (bruto >= desc && bruto > 0)
                TxtEditLiquido.Text = (bruto - desc).ToString("N2", cultura);
        }
        else if (temBrutoLiq)
        {
            // Bruto - Liquido -> Calcula Desconto!
            if (bruto >= liq && bruto > 0)
                TxtEditDescontos.Text = (bruto - liq).ToString("N2", cultura);
        }
        else if (tbAtual == TxtEditDescontos)
        {
            if (liq > 0 && bruto <= 0)
                TxtEditBruto.Text = (liq + desc).ToString("N2", cultura);
            else if (bruto >= desc && bruto > 0)
                TxtEditLiquido.Text = (bruto - desc).ToString("N2", cultura);
        }
    }

    private decimal LerDecimal(string? texto)
    {
        decimal.TryParse((texto ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v);
        return v;
    }

    private void BtnSalvar_Click(object sender, RoutedEventArgs e)
    {
        string natureza = ComboEditNatureza.SelectedItem?.ToString() ?? "Salário / Contracheque";
        bool ehReembolsoOuExtra = natureza == "Reembolso / Divisão Pix" || natureza == "Renda Extra";

        string nome = TxtEditNome.Text.Trim();
        string cpf = TxtEditCpf.Text.Trim();
        string desc = string.IsNullOrWhiteSpace(TxtEditDescricao.Text) ? natureza : TxtEditDescricao.Text.Trim();

        decimal bruto = ehReembolsoOuExtra ? 0m : LerDecimal(TxtEditBruto.Text);
        decimal descontos = ehReembolsoOuExtra ? 0m : LerDecimal(TxtEditDescontos.Text);
        decimal liquido = LerDecimal(TxtEditLiquido.Text);

        if (liquido <= 0 && bruto > 0)
            liquido = Math.Max(0m, bruto - descontos);
        if (bruto <= 0 && liquido > 0 && !ehReembolsoOuExtra)
            bruto = liquido + descontos;

        if (liquido <= 0)
        {
            JanelaMensagem.Exibir("Informe um valor maior que zero para salvar a alteração!", "Mini-Ordis // Edição", false, this);
            return;
        }

        if (string.IsNullOrWhiteSpace(nome))
            nome = ehReembolsoOuExtra ? natureza : "Titular Principal";

        if (!DateTime.TryParseExact(TxtEditData.Text.Trim(), "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dataFinal))
            dataFinal = _transacaoOriginal.Data;

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            UPDATE Transacoes SET
                Descricao = @Desc,
                Valor = @Val,
                ValorBruto = @Bruto,
                ValorDescontos = @DescRetido,
                Data = @Data,
                TitularNome = @Nome,
                TitularCpf = @Cpf,
                NaturezaReceita = @Nat
            WHERE Id = @Id;",
            new
            {
                _transacaoOriginal.Id,
                Desc = desc,
                Val = liquido,
                Bruto = bruto,
                DescRetido = descontos,
                Data = dataFinal.ToString("yyyy-MM-dd"),
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
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
