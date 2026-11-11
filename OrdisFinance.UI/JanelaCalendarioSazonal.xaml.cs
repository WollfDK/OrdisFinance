using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public class EventoSazonalViewModel : EventoSazonal
{
    public string DataFormatada => DataVencimento.ToString("dd/MM/yyyy");
    public string ValorAlvoTexto => $"R$ {ValorAlvo:N2}";

    public string StatusAntecedencia
    {
        get
        {
            int meses = ((DataVencimento.Year - DateTime.Now.Year) * 12) + DataVencimento.Month - DateTime.Now.Month;
            if (meses < 0) return $"Vencido há {Math.Abs(meses)} mês(es) • Alerta configurado: {MesesAntecedenciaAlerta} meses antes";
            if (meses == 0) return $"⚠ Vence neste mês! • Alerta configurado: {MesesAntecedenciaAlerta} meses antes";
            return $"Faltam {meses} mês(es) • Radar avisa com {MesesAntecedenciaAlerta} meses de antecedência";
        }
    }

    public string ReservaMensalTexto
    {
        get
        {
            int meses = Math.Max(1, ((DataVencimento.Year - DateTime.Now.Year) * 12) + DataVencimento.Month - DateTime.Now.Month);
            decimal porMes = Math.Round(ValorAlvo / meses, 2);
            return $"Reserva: R$ {porMes:N2} / mês";
        }
    }
}

public partial class JanelaCalendarioSazonal : Window
{
    private readonly CofreSqliteService _cofre;
    private int _idEmEdicao = 0;
    private bool _formatando = false;
    public bool HouveAlteracao { get; private set; } = false;

    public JanelaCalendarioSazonal(CofreSqliteService cofre)
    {
        InitializeComponent();
        _cofre = cofre;
        TxtDataSazonal.Text = DateTime.Now.AddMonths(3).ToString("dd/MM/yyyy");
        RecarregarLista();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void RecarregarLista()
    {
        var eventos = _cofre.ObterEventosSazonais()
            .Select(e => new EventoSazonalViewModel
            {
                Id = e.Id,
                Titulo = e.Titulo,
                ValorAlvo = e.ValorAlvo,
                DataVencimento = e.DataVencimento,
                MesesAntecedenciaAlerta = e.MesesAntecedenciaAlerta
            }).ToList();

        ListaCalendarioSazonal.ItemsSource = eventos;
    }

    private void ListaCalendarioSazonal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaCalendarioSazonal.SelectedItem is not EventoSazonalViewModel ev)
            return;

        _idEmEdicao = ev.Id;
        TxtTituloSazonal.Text = ev.Titulo;

        _formatando = true;
        TxtValorSazonal.Text = ev.ValorAlvo.ToString("N2", new CultureInfo("pt-BR"));
        TxtDataSazonal.Text = ev.DataVencimento.ToString("dd/MM/yyyy");
        _formatando = false;

        TxtMesesAntecedencia.Text = ev.MesesAntecedenciaAlerta.ToString();
        BtnSalvarSazonal.Content = "✔ ATUALIZAR EVENTO";
    }

    private void BtnLimparSazonal_Click(object sender, RoutedEventArgs e)
    {
        _idEmEdicao = 0;
        ListaCalendarioSazonal.SelectedIndex = -1;
        TxtTituloSazonal.Clear();
        _formatando = true;
        TxtValorSazonal.Clear();
        TxtDataSazonal.Text = DateTime.Now.AddMonths(3).ToString("dd/MM/yyyy");
        _formatando = false;
        TxtMesesAntecedencia.Text = "4";
        BtnSalvarSazonal.Content = "✔ SALVAR EVENTO";
    }

    private void ApenasDigitos_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TxtValorSazonal_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatando) return;
        _formatando = true;
        try
        {
            string d = Regex.Replace(TxtValorSazonal.Text ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(d) || !long.TryParse(d, out long centavos) || centavos == 0)
                TxtValorSazonal.Text = string.Empty;
            else
            {
                TxtValorSazonal.Text = (centavos / 100m).ToString("N2", new CultureInfo("pt-BR"));
                TxtValorSazonal.CaretIndex = TxtValorSazonal.Text.Length;
            }
        }
        finally { _formatando = false; }
    }

    private void TxtDataSazonal_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatando) return;
        _formatando = true;
        try
        {
            string d = Regex.Replace(TxtDataSazonal.Text ?? "", @"[^\d]", "");
            if (d.Length > 8) d = d.Substring(0, 8);
            TxtDataSazonal.Text = d.Length switch
            {
                <= 2 => d,
                <= 4 => $"{d.Substring(0, 2)}/{d.Substring(2)}",
                _ => $"{d.Substring(0, 2)}/{d.Substring(2, 2)}/{d.Substring(4)}"
            };
            TxtDataSazonal.CaretIndex = TxtDataSazonal.Text.Length;
        }
        finally { _formatando = false; }
    }

    private void BtnSalvarSazonal_Click(object sender, RoutedEventArgs e)
    {
        string titulo = TxtTituloSazonal.Text.Trim();
        decimal.TryParse((TxtValorSazonal.Text ?? "0").Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valor);
        int.TryParse(TxtMesesAntecedencia.Text.Trim(), out int mesesAnt);
        if (mesesAnt <= 0) mesesAnt = 4;

        if (string.IsNullOrWhiteSpace(titulo) || valor <= 0)
        {
            JanelaMensagem.Exibir("Preencha o Título do evento sazonal e um Valor Alvo maior que zero!", "Mini-Ordis // Calendário Sazonal", false, this);
            return;
        }

        if (!DateTime.TryParseExact(TxtDataSazonal.Text.Trim(), "dd/MM/yyyy", new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dtVenc))
        {
            JanelaMensagem.Exibir("Informe uma data de vencimento válida no formato dd/MM/aaaa!", "Mini-Ordis // Data Inválida", false, this);
            return;
        }

        _cofre.SalvarEventoSazonal(new EventoSazonal
        {
            Id = _idEmEdicao,
            Titulo = titulo,
            ValorAlvo = valor,
            DataVencimento = dtVenc,
            MesesAntecedenciaAlerta = mesesAnt
        });

        HouveAlteracao = true;
        BtnLimparSazonal_Click(sender, e);
        RecarregarLista();
        TxtStatusSazonal.Text = $"✔ Evento sazonal '{titulo}' salvo no calendário!";
    }

    private void BtnExcluirSazonal_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCalendarioSazonal.SelectedItem is not EventoSazonalViewModel ev)
        {
            JanelaMensagem.Exibir("Selecione um evento na lista para excluir!", "Mini-Ordis", false, this);
            return;
        }

        if (!JanelaMensagem.Exibir($"Deseja remover '{ev.Titulo}' do Calendário Sazonal?", "CONFIRMAR EXCLUSÃO", true, this))
            return;

        _cofre.ExcluirEventoSazonal(ev.Id);
        HouveAlteracao = true;
        BtnLimparSazonal_Click(sender, e);
        RecarregarLista();
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = HouveAlteracao;
        Close();
    }
}
