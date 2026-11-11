using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IOPath = System.IO.Path;

namespace OrdisFinance.UI;

public class ItemExploradorViewModel
{
    public string CaminhoCompleto { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool EhPasta { get; set; }
    public string TipoBadge { get; set; } = string.Empty;
    public Brush CorTipo { get; set; } = Brushes.Cyan;
    public Brush FundoBadge { get; set; } = Brushes.Black;
    public string DataFormatada { get; set; } = string.Empty;
    public Brush CorData { get; set; } = Brushes.Gray;
    public string TamanhoFormatado { get; set; } = string.Empty;
}

public class DiscoAtalhoViewModel
{
    public string NomeExibicao { get; set; } = string.Empty;
    public string CaminhoRaiz { get; set; } = string.Empty;
}

public partial class JanelaExploradorArquivos : Window
{
    private string _pastaAtual = string.Empty;
    public List<string> ArquivosSelecionados { get; private set; } = new();

    private static readonly string[] ExtensoesSuportadas = { ".pdf", ".txt", ".png", ".jpg", ".jpeg" };

    public JanelaExploradorArquivos()
    {
        InitializeComponent();
        CarregarUnidadesDeDisco();

        // Inicia direto na pasta Downloads do Operador (como na captura de referencia!)
        string pastaDownloads = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(pastaDownloads))
            pastaDownloads = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        NavegarParaPasta(pastaDownloads);
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CarregarUnidadesDeDisco()
    {
        var discos = new List<DiscoAtalhoViewModel>();
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
        {
            string rotulo = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco Local" : d.VolumeLabel;
            discos.Add(new DiscoAtalhoViewModel
            {
                NomeExibicao = $"💾  {d.Name} ({rotulo})",
                CaminhoRaiz = d.RootDirectory.FullName
            });
        }
        ListaDiscos.ItemsSource = discos;
    }

    private void NavegarParaPasta(string caminho)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            {
                JanelaMensagem.Exibir("O diretório informado não foi encontrado pelos sensores.", "Mini-Ordis // Explorador", false, this);
                return;
            }

            _pastaAtual = caminho;
            TxtCaminhoAtual.Text = _pastaAtual;
            AtualizarListagemArquivos();
        }
        catch (Exception ex)
        {
            JanelaMensagem.Exibir($"Acesso restrito a esta pasta: {ex.Message}", "Mini-Ordis // Permissão", false, this);
        }
    }

    private void AtualizarListagemArquivos()
    {
        if (string.IsNullOrWhiteSpace(_pastaAtual) || !Directory.Exists(_pastaAtual)) return;

        var itens = new List<ItemExploradorViewModel>();
        string termoBusca = (TxtBuscaArquivo?.Text ?? "").Trim().ToLowerInvariant();
        bool apenasSuportados = ChkApenasSuportados?.IsChecked == true;

        try
        {
            var dirInfo = new DirectoryInfo(_pastaAtual);

            // 1. Subpastas (ordenadas por nome)
            foreach (var sub in dirInfo.GetDirectories().OrderBy(d => d.Name))
            {
                if ((sub.Attributes & FileAttributes.Hidden) != 0) continue;
                if (!string.IsNullOrEmpty(termoBusca) && !sub.Name.ToLowerInvariant().Contains(termoBusca)) continue;

                itens.Add(new ItemExploradorViewModel
                {
                    CaminhoCompleto = sub.FullName,
                    Nome = sub.Name,
                    EhPasta = true,
                    TipoBadge = "📁 PASTA",
                    CorTipo = new SolidColorBrush(Color.FromRgb(255, 184, 0)),
                    FundoBadge = new SolidColorBrush(Color.FromRgb(28, 22, 8)),
                    DataFormatada = FormatarData(sub.LastWriteTime, out var corDt),
                    CorData = corDt,
                    TamanhoFormatado = "<DIR>"
                });
            }

            // 2. Arquivos (ordenados dos mais recentes "Hoje" para os mais antigos!)
            foreach (var arq in dirInfo.GetFiles().OrderByDescending(f => f.LastWriteTime))
            {
                if ((arq.Attributes & FileAttributes.Hidden) != 0) continue;

                string ext = arq.Extension.ToLowerInvariant();
                if (apenasSuportados && !ExtensoesSuportadas.Contains(ext))
                    continue;

                if (!string.IsNullOrEmpty(termoBusca) && !arq.Name.ToLowerInvariant().Contains(termoBusca))
                    continue;

                var (badge, corBadge, fundoBadge) = ObterEstiloPorExtensao(ext);

                itens.Add(new ItemExploradorViewModel
                {
                    CaminhoCompleto = arq.FullName,
                    Nome = arq.Name,
                    EhPasta = false,
                    TipoBadge = badge,
                    CorTipo = corBadge,
                    FundoBadge = fundoBadge,
                    DataFormatada = FormatarData(arq.LastWriteTime, out var corDt),
                    CorData = corDt,
                    TamanhoFormatado = FormatarTamanho(arq.Length)
                });
            }
        }
        catch { }

        ListaArquivos.ItemsSource = itens;
        TxtArquivoSelecionado.Clear();
    }

    private (string Badge, Brush Cor, Brush Fundo) ObterEstiloPorExtensao(string ext)
    {
        return ext switch
        {
            ".pdf" => ("📄 PDF", new SolidColorBrush(Color.FromRgb(0, 240, 255)), new SolidColorBrush(Color.FromRgb(8, 26, 36))),
            ".png" or ".jpg" or ".jpeg" => ("🖼 IMG", new SolidColorBrush(Color.FromRgb(0, 255, 102)), new SolidColorBrush(Color.FromRgb(8, 30, 18))),
            ".txt" => ("📝 TXT", new SolidColorBrush(Color.FromRgb(210, 232, 255)), new SolidColorBrush(Color.FromRgb(18, 24, 34))),
            _ => ("📦 ARQ", new SolidColorBrush(Color.FromRgb(157, 0, 255)), new SolidColorBrush(Color.FromRgb(22, 10, 34)))
        };
    }

    private string FormatarData(DateTime dt, out Brush cor)
    {
        if (dt.Date == DateTime.Today)
        {
            cor = new SolidColorBrush(Color.FromRgb(0, 255, 102));
            return $"[HOJE] {dt:HH:mm}";
        }
        cor = new SolidColorBrush(Color.FromRgb(138, 164, 192));
        return dt.ToString("dd/MM/yyyy HH:mm");
    }

    private string FormatarTamanho(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:N1} KB";
        double mb = kb / 1024.0;
        return $"{mb:N2} MB";
    }

    private void BtnSubirPasta_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pastaAtual)) return;
        var pai = Directory.GetParent(_pastaAtual);
        if (pai != null)
            NavegarParaPasta(pai.FullName);
    }

    private void TxtCaminhoAtual_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            NavegarParaPasta(TxtCaminhoAtual.Text.Trim());
    }

    private void TxtBuscaArquivo_TextChanged(object sender, TextChangedEventArgs e) => AtualizarListagemArquivos();
    private void FiltroSuportados_Changed(object sender, RoutedEventArgs e) => AtualizarListagemArquivos();

    private void BtnAtalhoDownloads_Click(object sender, RoutedEventArgs e)
    {
        string p = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        NavegarParaPasta(p);
    }

    private void BtnAtalhoDesktop_Click(object sender, RoutedEventArgs e)
    {
        NavegarParaPasta(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    }

    private void BtnAtalhoDocumentos_Click(object sender, RoutedEventArgs e)
    {
        NavegarParaPasta(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    private void BtnAtalhoDisco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string raiz)
            NavegarParaPasta(raiz);
    }

    private void ListaArquivos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var arquivos = ListaArquivos.SelectedItems
            .OfType<ItemExploradorViewModel>()
            .Where(x => !x.EhPasta)
            .ToList();

        if (arquivos.Count == 1)
            TxtArquivoSelecionado.Text = arquivos[0].Nome;
        else if (arquivos.Count > 1)
            TxtArquivoSelecionado.Text = $"{arquivos.Count} arquivos selecionados para importação em lote";
        else
            TxtArquivoSelecionado.Clear();
    }

    private void ListaArquivos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListaArquivos.SelectedItem is not ItemExploradorViewModel item) return;

        if (item.EhPasta)
        {
            NavegarParaPasta(item.CaminhoCompleto);
        }
        else
        {
            ArquivosSelecionados = new List<string> { item.CaminhoCompleto };
            DialogResult = true;
            Close();
        }
    }

    private void BtnImportar_Click(object sender, RoutedEventArgs e)
    {
        // Se selecionou uma pasta e clicou em Importar, entra na pasta
        if (ListaArquivos.SelectedItems.Count == 1 && ListaArquivos.SelectedItem is ItemExploradorViewModel unico && unico.EhPasta)
        {
            NavegarParaPasta(unico.CaminhoCompleto);
            return;
        }

        var selecionados = ListaArquivos.SelectedItems
            .OfType<ItemExploradorViewModel>()
            .Where(x => !x.EhPasta)
            .Select(x => x.CaminhoCompleto)
            .ToList();

        if (!selecionados.Any())
        {
            JanelaMensagem.Exibir("Selecione pelo menos um arquivo (PDF, Imagem ou TXT) na lista para importar!", "Mini-Ordis // Explorador", false, this);
            return;
        }

        ArquivosSelecionados = selecionados;
        DialogResult = true;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
