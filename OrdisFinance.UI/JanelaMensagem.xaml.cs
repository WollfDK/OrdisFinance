using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OrdisFinance.UI;

public partial class JanelaMensagem : Window
{
    public JanelaMensagem(string mensagem, string titulo, bool modoConfirmacaoSimNao, bool alertaCritico)
    {
        InitializeComponent();

        TxtMensagem.Text = mensagem;
        TxtTituloJanela.Text = string.IsNullOrWhiteSpace(titulo) ? "MINI-ORDIS // COMUNICADO" : titulo.ToUpperInvariant();

        if (alertaCritico || modoConfirmacaoSimNao)
        {
            var corAlerta = Color.FromRgb(255, 184, 0);
            BordaExterna.BorderBrush = new SolidColorBrush(corAlerta);
            TxtTituloJanela.Foreground = new SolidColorBrush(corAlerta);
            TxtIconeTitulo.Text = "⚠";
            TxtIconeTitulo.Foreground = new SolidColorBrush(corAlerta);
            SombraJanela.Color = corAlerta;
        }

        if (modoConfirmacaoSimNao)
        {
            BtnCancelar.Visibility = Visibility.Visible;
            BtnCancelar.Content = "✖ NÃO / CANCELAR";
            BtnConfirmar.Content = "✔ SIM, EXECUTAR";
        }
        else
        {
            BtnCancelar.Visibility = Visibility.Collapsed;
            BtnConfirmar.Content = "✔ ENTENDIDO";
        }
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static bool Exibir(string mensagem, string titulo = "MINI-ORDIS // COMUNICADO", bool confirmarSimNao = false, Window? owner = null)
    {
        bool critico = titulo.Contains("Erro", StringComparison.OrdinalIgnoreCase) ||
                       titulo.Contains("Alerta", StringComparison.OrdinalIgnoreCase) ||
                       mensagem.Contains("⚠");

        var janela = new JanelaMensagem(mensagem, titulo, confirmarSimNao, critico);

        Window? janelaMae = owner ?? System.Windows.Application.Current?.MainWindow;
        if (janelaMae != null && janelaMae.IsLoaded && janelaMae.IsVisible && janelaMae != janela)
        {
            janela.Owner = janelaMae;
            janela.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return janela.ShowDialog() == true;
    }
}

