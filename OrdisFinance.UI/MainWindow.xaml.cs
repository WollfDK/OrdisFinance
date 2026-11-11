using System.Windows.Threading;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Dapper;
using Microsoft.Win32;
using OrdisFinance.Domain;
using OrdisFinance.Infrastructure;
using Path = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;

namespace OrdisFinance.UI;

public class QuarentenaViewModel : ItemQuarentena
{
    public string ResumoVisual => $"[ID {Id}] {DescricaoSugerida} | R$ {ValorSugerido:N2} ({CategoriaSugerida})";
}

public class LegendaTopItem
{
    public string Nome { get; set; } = string.Empty;
    public string ValorFormatado { get; set; } = string.Empty;
    public Brush CorBrush { get; set; } = Brushes.Cyan;
}

public class DadoAgrupadoGrafico
{
    public string Chave { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OpcaoSugestaoDescricao
{
    public string TextoMenu { get; set; } = string.Empty;
    public SugestaoDescricaoHistorico Dados { get; set; } = new();
    public override string ToString() => TextoMenu;
}

public partial class MainWindow : Window
{
    private readonly IdentidadeOperadorService _identidade;
    private readonly OrdisUpdateService _updateService;
    private readonly DispatcherTimer _timerRelogioTopo;
    private bool _modoFurtivo = false;
    private readonly CofreSqliteService _cofre;
    private readonly IngestaoService _ingestao;
    private readonly MiniOrdisAdvisorService _advisor;
    private readonly FaturaParserService _faturaParser;
    private readonly string _pastaBackups;
    private QuarentenaViewModel? _itemSelecionado;
    private bool _formatandoValor = false;
    private bool _processandoAutoDesc = false;
    private bool _ultimoTecladoFoiApagarDesc = false;
    private List<SugestaoDescricaoHistorico> _memoriaDescricoes = new();
    private bool _inicializandoFiltros = true;

    private static readonly Color[] PaletaCyberpunk = {
        Color.FromRgb(0, 240, 255),  // 1. Ciano Neon
        Color.FromRgb(0, 255, 102),  // 2. Verde Neon
        Color.FromRgb(255, 0, 85),   // 3. Magenta/Rosa Neon
        Color.FromRgb(255, 184, 0),  // 4. Amarelo Cyber
        Color.FromRgb(157, 0, 255)   // 5. Roxo Neon (Outros)
    };

    private static readonly string[] NomesMeses = {
        "01 - Jan", "02 - Fev", "03 - Mar", "04 - Abr", "05 - Mai", "06 - Jun",
        "07 - Jul", "08 - Ago", "09 - Set", "10 - Out", "11 - Nov", "12 - Dez"
    };

        public MainWindow()
    {
        // Impede que o fechamento do modal de Login encerre o processo antes da MainWindow abrir!
        if (global::System.Windows.Application.Current != null)
        {
            global::System.Windows.Application.Current.MainWindow = this;
            global::System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        InitializeComponent();

        string pastaBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CofreDados");
        _identidade = new IdentidadeOperadorService(pastaBase);
        _updateService = new OrdisUpdateService(pastaBase);

        var janelaAuth = new JanelaAcessoCofre(_identidade);
        if (janelaAuth.ShowDialog() != true)
        {
            Environment.Exit(0);
            return;
        }

        string arquivoDb = Path.Combine(pastaBase, "OrdisFinance.db");
        _pastaBackups = Path.Combine(pastaBase, "Backups");
        string arquivoLua = Path.Combine(pastaBase, "Scripts", "triagem.lua");

        _cofre = new CofreSqliteService(arquivoDb, _identidade.ObterChaveBancoInterna());
        _cofre.InicializarCofre();
        _ingestao = new IngestaoService(_cofre, arquivoLua);
        _advisor = new MiniOrdisAdvisorService(_cofre);
        _faturaParser = new FaturaParserService(_cofre);

        ComboFormaPagamento.ItemsSource = new List<string> { "Pix", "Cartão de Crédito", "Débito", "Dinheiro" };

        AtualizarCabecalhoIdentidade();

        _timerRelogioTopo = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timerRelogioTopo.Tick += (_, _) => AtualizarRelogioCyberpunk();
        AtualizarRelogioCyberpunk();
        _timerRelogioTopo.Start();

        InicializarSeletoresDataGraficos();
        RecarregarListaCategorias();
        AtualizarPainelCompleto();

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void AtualizarCabecalhoIdentidade()
    {
        if (_identidade.IdentidadeAtual != null && TxtIdentidadeCofre != null)
        {
            string cod = _identidade.IdentidadeAtual.CodigoCofre;
            string op = _identidade.IdentidadeAtual.NomeOperador.ToUpperInvariant();
            TxtIdentidadeCofre.Text = $"COFRE {cod}: CONECTADO // OPERADOR {op}";
        }
    }

    private void AtualizarRelogioCyberpunk()
    {
        var agora = DateTime.Now;
        string[] dias = { "DOM", "SEG", "TER", "QUA", "QUI", "SEX", "SÁB" };
        string[] meses = { "JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ" };

        TxtDataCyberpunk.Text = $"{dias[(int)agora.DayOfWeek]} // {agora.Day:D2}.{meses[agora.Month - 1]}.{agora.Year}";
        TxtHoraMinutoCyber.Text = agora.ToString("HH:mm");
        TxtSegundosCyber.Text = $":{agora:ss}";
    }

    // EASTER EGG: Clicar em "COFRE XXX-YYYY: CONECTADO // OPERADOR ..." abre a ficha secreta de inspecao/credenciais!
    private void TxtIdentidadeCofre_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var modal = new JanelaAcessoCofre(_identidade, modoInspecaoEasterEgg: true) { Owner = this };
        if (modal.ShowDialog() == true)
        {
            AtualizarCabecalhoIdentidade();
            TxtStatusCofre.Text = "CREDENCIAIS DO OPERADOR ATUALIZADAS COM SUCESSO!";
        }
    }

    private async void BtnUpdateOnline_Click(object sender, RoutedEventArgs e)
    {
        BtnUpdateOnline.IsEnabled = false;
        BtnUpdateOnline.Content = "⏳ CHECANDO GIT...";
        TxtStatusCofre.Text = $"CONSULTANDO SERVIDOR GITHUB ({_updateService.ObterRepositorioConfigurado()})...";

        var res = await _updateService.VerificarAtualizacaoOnlineAsync();
        BtnUpdateOnline.IsEnabled = true;

        if (res.IsFailed)
        {
            BtnUpdateOnline.Content = "☁ ATUALIZAR (v2.7)";
            JanelaMensagem.Exibir(
                $"📡 STATUS DO AUTO-UPDATER ONLINE:\n\n" +
                $"• Versão Atual Instalada: v{OrdisUpdateService.VersaoAtualSistema}\n" +
                $"• Repositório Alvo: github.com/{_updateService.ObterRepositorioConfigurado()}\n" +
                $"• Detalhe: {res.Errors[0].Message}\n\n" +
                $"Use o script 'Publicar-Atualizacao-GitHub.ps1' na pasta do projeto sempre que quiser lançar uma nova versão online para todas as suas máquinas!",
                "Mini-Ordis // Auto-Updater Online",
                false,
                this);
            return;
        }

        var info = res.Value;
        if (!info.TemAtualizacaoDisponivel)
        {
            BtnUpdateOnline.Content = "✔ ATUALIZADO (v2.7)";
            TxtStatusCofre.Text = $"SISTEMA NA ÚLTIMA VERSÃO OFICIAL ({info.VersaoTag})!";
            JanelaMensagem.Exibir(
                $"✔ O seu Cephalon Ordis já está na versão mais recente publicada!\n\n" +
                $"• Versão Instalada: v{OrdisUpdateService.VersaoAtualSistema}\n" +
                $"• Última Release no GitHub: {info.VersaoTag} ({info.Titulo})",
                "Mini-Ordis // Sistema Atualizado",
                false,
                this);
            return;
        }

        BtnUpdateOnline.Content = $"⚡ BAIXAR {info.VersaoTag}";
        if (JanelaMensagem.Exibir(
            $"🚀 NOVA ATUALIZAÇÃO DISPONÍVEL NO GITHUB!\n\n" +
            $"• Sua Versão: v{OrdisUpdateService.VersaoAtualSistema}\n" +
            $"• Nova Versão: {info.VersaoTag} ({info.Titulo})\n" +
            $"• Notas: {info.Notas}\n\n" +
            $"Deseja baixar e atualizar automaticamente agora? (Seus dados e senhas do Cofre permanecerão 100% intactos!)",
            "ATUALIZAÇÃO ONLINE DISPONÍVEL",
            confirmarSimNao: true,
            owner: this))
        {
            TxtStatusCofre.Text = $"BAIXANDO E APLICANDO ATUALIZAÇÃO {info.VersaoTag}...";
            var resDownload = await _updateService.BaixarEAplicarAtualizacaoQuenteAsync(info.UrlDownloadZip);
            if (resDownload.IsFailed)
            {
                JanelaMensagem.Exibir(resDownload.Errors[0].Message, "Mini-Ordis // Erro no Update", false, this);
            }
        }
    }

    private void BtnModoFurtivo_Click(object sender, RoutedEventArgs e)
    {
        _modoFurtivo = !_modoFurtivo;
        BtnModoFurtivo.Content = _modoFurtivo ? "🙈 FURTIVO: ON" : "👁 FURTIVO";
        AtualizarPainelCompleto();
        TxtStatusCofre.Text = _modoFurtivo ? "MODO FURTIVO ATIVADO (VALORES OCULTOS)" : "MODO FURTIVO DESATIVADO";
    }

    private void BtnTrancarTerminal_Click(object sender, RoutedEventArgs e)
    {
        global::System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Visibility = Visibility.Hidden;

        var janelaLock = new JanelaAcessoCofre(_identidade);
        if (janelaLock.ShowDialog() == true)
        {
            AtualizarCabecalhoIdentidade();
            Visibility = Visibility.Visible;
            global::System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            TxtStatusCofre.Text = "TERMINAL DESTRANCADO PELO OPERADOR!";
        }
        else
        {
            Environment.Exit(0);
        }
    }

        private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BtnMaximizarJanela_Click(sender, e);
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimizarJanela_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximizarJanela_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnFecharJanela_Click(object sender, RoutedEventArgs e) => Environment.Exit(0);

    private void InicializarSeletoresDataGraficos()
    {
        _inicializandoFiltros = true;

        ComboMesPizza.ItemsSource = NomesMeses;
        ComboMesPizza.SelectedIndex = DateTime.Now.Month - 1;

        var anos = new List<string> { "2025", "2026", "2027" };
        ComboAnoPizza.ItemsSource = anos;
        ComboAnoLinha.ItemsSource = anos;

        string anoAtual = DateTime.Now.Year.ToString();
        ComboAnoPizza.SelectedItem = anos.Contains(anoAtual) ? anoAtual : "2026";
        ComboAnoLinha.SelectedItem = anos.Contains(anoAtual) ? anoAtual : "2026";

        _inicializandoFiltros = false;
    }

    private void FiltroPizza_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inicializandoFiltros) return;
        AtualizarGraficosPizza();
    }

    private void FiltroLinha_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inicializandoFiltros) return;
        AtualizarGraficoLinhas();
    }

    private void CanvasGraficoLinhas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AtualizarGraficoLinhas();
    }

    // Sempre que o Operador clicar na seta de Categoria, sincroniza ao vivo com o Cofre!
    private void ComboEditCategoria_DropDownOpened(object sender, EventArgs e)
    {
        string? atual = ComboEditCategoria.SelectedItem?.ToString();
        RecarregarListaCategorias(atual);
    }

    private void RecarregarListaCategorias(string? selecionarCategoria = null)
    {
        var categorias = _cofre.ObterCategorias();
        ComboEditCategoria.ItemsSource = null;
        ComboEditCategoria.ItemsSource = categorias;

        if (!string.IsNullOrWhiteSpace(selecionarCategoria))
        {
            var match = categorias.FirstOrDefault(c => c.Equals(selecionarCategoria, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ComboEditCategoria.SelectedItem = match;
            }
        }
    }

    // Abre a Central de Categorias preservando o que estiver digitado nas barras acima
    private void BtnAddCategoria_Click(object sender, RoutedEventArgs e)
    {
        string descDigitada = TxtEditDescricao.Text;
        string valorDigitado = TxtEditValor.Text;
        string? catAntes = ComboEditCategoria.SelectedItem?.ToString();
        string? formaAntes = ComboFormaPagamento.SelectedItem?.ToString();

        var popup = new JanelaNovaCategoria(_cofre) { Owner = this };
        popup.ShowDialog();

        // Independentemente de fechar pelo botao ou pelo 'X', recarrega as categorias do Cofre!
        string? alvoSelecionar = !string.IsNullOrWhiteSpace(popup.CategoriaCriada) ? popup.CategoriaCriada : catAntes;
        RecarregarListaCategorias(alvoSelecionar);

        if (popup.HouveAlteracao)
        {
            AtualizarGraficosPizza();
            AtualizarGraficoLinhas();
            TxtStatusCofre.Text = "MATRIZ DE CATEGORIAS SINCRONIZADA COM O COFRE!";
        }

        // Restaura o que o Operador estava digitando antes de abrir a janela
        TxtEditDescricao.Text = descDigitada;
        _formatandoValor = true;
        TxtEditValor.Text = valorDigitado;
        _formatandoValor = false;
        if (!string.IsNullOrWhiteSpace(formaAntes))
            ComboFormaPagamento.SelectedItem = formaAntes;
    }

    private void AtualizarGraficosPizza()
    {
        if (ComboMesPizza.SelectedIndex < 0 || ComboAnoPizza.SelectedItem == null) return;

        int mes = ComboMesPizza.SelectedIndex + 1;
        string ano = ComboAnoPizza.SelectedItem.ToString() ?? "2026";
        string prefixoAnoMes = $"{ano}-{mes:D2}";

        using var conn = _cofre.CriarConexaoAberta();

        var porCategoria = conn.Query<DadoAgrupadoGrafico>(@"
            SELECT Categoria AS Chave, COALESCE(SUM(Valor), 0) AS Total
            FROM Transacoes
            WHERE Tipo = 2 AND substr(Data, 1, 7) = @AnoMes
            GROUP BY Categoria
            ORDER BY Total DESC;", new { AnoMes = prefixoAnoMes }).ToList();

        DesenharPizzaComTop4(CanvasPizzaCategorias, LegendaTopCategorias, porCategoria);

        var porPagamento = conn.Query<DadoAgrupadoGrafico>(@"
            SELECT COALESCE(NULLIF(FormaPagamento, ''), 'Pix') AS Chave, COALESCE(SUM(Valor), 0) AS Total
            FROM Transacoes
            WHERE Tipo = 2 AND substr(Data, 1, 7) = @AnoMes
            GROUP BY COALESCE(NULLIF(FormaPagamento, ''), 'Pix')
            ORDER BY Total DESC;", new { AnoMes = prefixoAnoMes }).ToList();

                DesenharPizzaComTop4(CanvasPizzaPagamentos, LegendaTopPagamentos, porPagamento);

        var porPessoa = conn.Query<DadoAgrupadoGrafico>(@"
            SELECT COALESCE(NULLIF(TRIM(TitularNome), ''), Descricao) AS Chave, COALESCE(SUM(Valor), 0) AS Total
            FROM Transacoes
            WHERE Tipo = 1 AND substr(Data, 1, 7) = @AnoMes AND COALESCE(NaturezaReceita, '') != 'Reembolso / Divisão Pix'
            GROUP BY COALESCE(NULLIF(TRIM(TitularNome), ''), Descricao)
            ORDER BY Total DESC;", new { AnoMes = prefixoAnoMes }).ToList();

        DesenharPizzaComTop4(CanvasPizzaGanhosPessoa, LegendaTopGanhosPessoa, porPessoa);
    }

    private void DesenharPizzaComTop4(Canvas canvas, ItemsControl legendaControl, List<DadoAgrupadoGrafico> dados)
    {
        canvas.Children.Clear();
        var listaLegenda = new List<LegendaTopItem>();

        decimal somaTotal = dados.Sum(x => x.Total);
        double tamanho = 116;
        double cx = tamanho / 2;
        double cy = tamanho / 2;
        double raio = 50;

        if (somaTotal <= 0 || !dados.Any())
        {
            var circuloVazio = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Stroke = new SolidColorBrush(Color.FromRgb(30, 45, 65)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromRgb(12, 14, 22))
            };
            Canvas.SetLeft(circuloVazio, cx - raio);
            Canvas.SetTop(circuloVazio, cy - raio);
            canvas.Children.Add(circuloVazio);

            listaLegenda.Add(new LegendaTopItem
            {
                Nome = "Sem registros no mês",
                ValorFormatado = "R$ 0,00",
                CorBrush = new SolidColorBrush(Color.FromRgb(90, 114, 138))
            });
            legendaControl.ItemsSource = listaLegenda;
            return;
        }

        var top4 = dados.Take(4).ToList();
        var fatiasParaDesenhar = new List<DadoAgrupadoGrafico>(top4);
        decimal resto = dados.Skip(4).Sum(x => x.Total);
        if (resto > 0)
            fatiasParaDesenhar.Add(new DadoAgrupadoGrafico { Chave = "Outros", Total = resto });

        double anguloAtual = -90;

        for (int i = 0; i < fatiasParaDesenhar.Count; i++)
        {
            var item = fatiasParaDesenhar[i];
            double proporcao = (double)(item.Total / somaTotal);
            double anguloFatia = proporcao * 360.0;
            Color cor = PaletaCyberpunk[Math.Min(i, PaletaCyberpunk.Length - 1)];
            var brushCor = new SolidColorBrush(cor);

            if (anguloFatia >= 359.9)
            {
                var completo = new Ellipse
                {
                    Width = raio * 2,
                    Height = raio * 2,
                    Fill = brushCor,
                    Stroke = new SolidColorBrush(Color.FromRgb(8, 8, 12)),
                    StrokeThickness = 1.5,
                    ToolTip = $"{item.Chave}: R$ {item.Total:N2} (100%)"
                };
                Canvas.SetLeft(completo, cx - raio);
                Canvas.SetTop(completo, cy - raio);
                canvas.Children.Add(completo);
            }
            else if (anguloFatia > 0.5)
            {
                double radInicio = anguloAtual * Math.PI / 180.0;
                double radFim = (anguloAtual + anguloFatia) * Math.PI / 180.0;

                Point pInicio = new Point(cx + raio * Math.Cos(radInicio), cy + raio * Math.Sin(radInicio));
                Point pFim = new Point(cx + raio * Math.Cos(radFim), cy + raio * Math.Sin(radFim));

                var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
                fig.Segments.Add(new LineSegment(pInicio, true));
                fig.Segments.Add(new ArcSegment(
                    pFim,
                    new Size(raio, raio),
                    0,
                    anguloFatia > 180.0,
                    SweepDirection.Clockwise,
                    true));

                var geo = new PathGeometry();
                geo.Figures.Add(fig);

                var pathFatia = new WpfPath
                {
                    Data = geo,
                    Fill = brushCor,
                    Stroke = new SolidColorBrush(Color.FromRgb(8, 8, 12)),
                    StrokeThickness = 1.5,
                    ToolTip = $"{item.Chave}: R$ {item.Total:N2} ({proporcao * 100:N1}%)"
                };
                canvas.Children.Add(pathFatia);
            }

            if (i < 4)
            {
                listaLegenda.Add(new LegendaTopItem
                {
                    Nome = $"{i + 1}. {item.Chave}",
                    ValorFormatado = $"R$ {item.Total:N0} ({proporcao * 100:N0}%)",
                    CorBrush = brushCor
                });
            }

            anguloAtual += anguloFatia;
        }

        var miolo = new Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = new SolidColorBrush(Color.FromRgb(8, 8, 12)),
            Stroke = new SolidColorBrush(Color.FromRgb(22, 34, 50)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(miolo, cx - 14);
        Canvas.SetTop(miolo, cy - 14);
        canvas.Children.Add(miolo);

        legendaControl.ItemsSource = listaLegenda;
    }

    private void AtualizarGraficoLinhas()
    {
        if (CanvasGraficoLinhas == null || ComboAnoLinha.SelectedItem == null) return;

        CanvasGraficoLinhas.Children.Clear();
        double w = CanvasGraficoLinhas.ActualWidth;
        double h = CanvasGraficoLinhas.ActualHeight;
        if (w < 120 || h < 80) return;

        string ano = ComboAnoLinha.SelectedItem.ToString() ?? "2026";
        using var conn = _cofre.CriarConexaoAberta();

        decimal[] ganhosMes = new decimal[12];
        decimal[] gastosMes = new decimal[12];

        for (int m = 1; m <= 12; m++)
        {
            string ym = $"{ano}-{m:D2}";
            var balancoMes = _cofre.CalcularBalancoMes(ym);
            gastosMes[m - 1] = balancoMes.GastosLiquidos;
            ganhosMes[m - 1] = balancoMes.RendaLiquidaReal;
        }

                decimal somaGanhosAno = ganhosMes.Sum();
        decimal somaGastosAno = gastosMes.Sum();
        if (TxtLegendaGanhosValor != null)
            TxtLegendaGanhosValor.Text = $"Ganhos: R$ {somaGanhosAno:N2}";
        if (TxtLegendaGastosValor != null)
            TxtLegendaGastosValor.Text = $"Gastos: R$ {somaGastosAno:N2}";

        decimal maiorValor = Math.Max(5000m, Math.Max(ganhosMes.Max(), gastosMes.Max()));
        double tetoY = Math.Ceiling((double)maiorValor / 1000.0) * 1000.0;
        if (tetoY <= 0) tetoY = 5000;

        double padLeft = 48;
        double padRight = 16;
        double padTop = 14;
        double padBottom = 26;

        double plotW = w - padLeft - padRight;
        double plotH = h - padTop - padBottom;

        int degraus = 4;
        for (int i = 0; i <= degraus; i++)
        {
            double frac = i / (double)degraus;
            double yPos = padTop + plotH - (frac * plotH);
            double valorEixo = frac * tetoY;

            var linhaGrade = new Line
            {
                X1 = padLeft,
                Y1 = yPos,
                X2 = padLeft + plotW,
                Y2 = yPos,
                Stroke = new SolidColorBrush(i == 0 ? Color.FromRgb(50, 75, 100) : Color.FromRgb(22, 32, 46)),
                StrokeThickness = i == 0 ? 1.4 : 1
            };
            CanvasGraficoLinhas.Children.Add(linhaGrade);

            var lblY = new TextBlock
            {
                Text = valorEixo.ToString("N0", new CultureInfo("pt-BR")),
                Foreground = new SolidColorBrush(Color.FromRgb(120, 145, 170)),
                FontSize = 10
            };
            Canvas.SetLeft(lblY, 4);
            Canvas.SetTop(lblY, yPos - 7);
            CanvasGraficoLinhas.Children.Add(lblY);
        }

        var eixoY = new Line
        {
            X1 = padLeft,
            Y1 = padTop,
            X2 = padLeft,
            Y2 = padTop + plotH,
            Stroke = new SolidColorBrush(Color.FromRgb(50, 75, 100)),
            StrokeThickness = 1.4
        };
        CanvasGraficoLinhas.Children.Add(eixoY);

        string[] siglasMes = { "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez" };
        var polyGanhos = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 240, 255)),
            StrokeThickness = 2.2
        };
        var polyGastos = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 0, 85)),
            StrokeThickness = 2.2
        };

        var pontosGanhos = new List<(Point Pt, string Label, decimal Val)>();
        var pontosGastos = new List<(Point Pt, string Label, decimal Val)>();

        for (int m = 0; m < 12; m++)
        {
            double x = padLeft + (m / 11.0) * plotW;

            var lblMes = new TextBlock
            {
                Text = siglasMes[m],
                Foreground = new SolidColorBrush(Color.FromRgb(138, 164, 192)),
                FontSize = 10
            };
            Canvas.SetLeft(lblMes, x - 10);
            Canvas.SetTop(lblMes, padTop + plotH + 6);
            CanvasGraficoLinhas.Children.Add(lblMes);

            double yGanho = padTop + plotH - (((double)ganhosMes[m] / tetoY) * plotH);
            double yGasto = padTop + plotH - (((double)gastosMes[m] / tetoY) * plotH);

            Point ptGanho = new Point(x, yGanho);
            Point ptGasto = new Point(x, yGasto);

            polyGanhos.Points.Add(ptGanho);
            polyGastos.Points.Add(ptGasto);

            pontosGanhos.Add((ptGanho, siglasMes[m], ganhosMes[m]));
            pontosGastos.Add((ptGasto, siglasMes[m], gastosMes[m]));
        }

        CanvasGraficoLinhas.Children.Add(polyGanhos);
        CanvasGraficoLinhas.Children.Add(polyGastos);

        foreach (var p in pontosGanhos)
        {
            var nodo = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(8, 8, 12)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 240, 255)),
                StrokeThickness = 2,
                ToolTip = $"{p.Label}/{ano} — Ganhos: R$ {p.Val:N2}"
            };
            Canvas.SetLeft(nodo, p.Pt.X - 4);
            Canvas.SetTop(nodo, p.Pt.Y - 4);
            CanvasGraficoLinhas.Children.Add(nodo);
        }

        foreach (var p in pontosGastos)
        {
            var nodo = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(8, 8, 12)),
                Stroke = new SolidColorBrush(Color.FromRgb(255, 0, 85)),
                StrokeThickness = 2,
                ToolTip = $"{p.Label}/{ano} — Gastos: R$ {p.Val:N2}"
            };
            Canvas.SetLeft(nodo, p.Pt.X - 4);
            Canvas.SetTop(nodo, p.Pt.Y - 4);
            CanvasGraficoLinhas.Children.Add(nodo);
        }
    }

    // AUTO-COMPLETE NEURAL DA BARRA DESCRICAO (PREENCHE CATEGORIA, PAGAMENTO E VALOR VINCULADO!)
    private void TxtEditDescricao_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _ultimoTecladoFoiApagarDesc = (e.Key == Key.Back || e.Key == Key.Delete);

        if (PopupAutoCompleteDescricao.IsOpen)
        {
            if (e.Key == Key.Down && ListaSugestoesDescricao.Items.Count > 0)
            {
                ListaSugestoesDescricao.SelectedIndex = Math.Min(ListaSugestoesDescricao.SelectedIndex + 1, ListaSugestoesDescricao.Items.Count - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ListaSugestoesDescricao.Items.Count > 0)
            {
                ListaSugestoesDescricao.SelectedIndex = Math.Max(ListaSugestoesDescricao.SelectedIndex - 1, 0);
                e.Handled = true;
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Tab) && ListaSugestoesDescricao.SelectedItem is OpcaoSugestaoDescricao sel)
            {
                AplicarSugestaoDescricaoCompleta(sel.Dados, selecionarRestoTexto: false);
                PopupAutoCompleteDescricao.IsOpen = false;
                if (e.Key == Key.Enter) e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                PopupAutoCompleteDescricao.IsOpen = false;
                e.Handled = true;
            }
        }
    }

    private void TxtEditDescricao_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_processandoAutoDesc || _inicializandoFiltros || _itemSelecionado != null) return;

        string digitado = TxtEditDescricao.Text;
        if (string.IsNullOrWhiteSpace(digitado))
        {
            PopupAutoCompleteDescricao.IsOpen = false;
            return;
        }

        if (!_memoriaDescricoes.Any())
            _memoriaDescricoes = _cofre.ObterSugestoesDescricao();

        var matches = _memoriaDescricoes
            .Where(x => x.Descricao.Contains(digitado.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(x => new OpcaoSugestaoDescricao
            {
                Dados = x,
                TextoMenu = $"⚡ {x.Descricao} — [{x.Categoria} // {x.FormaPagamento}]{(x.UltimoValor > 0 ? $" (Último: R$ {x.UltimoValor:N2})" : "")}"
            })
            .ToList();

        if (matches.Any())
        {
            ListaSugestoesDescricao.ItemsSource = matches;
            ListaSugestoesDescricao.SelectedIndex = 0;
            PopupAutoCompleteDescricao.IsOpen = true;

            var matchPrefixo = _memoriaDescricoes.FirstOrDefault(x => x.Descricao.StartsWith(digitado, StringComparison.OrdinalIgnoreCase));
            if (matchPrefixo != null && !_ultimoTecladoFoiApagarDesc && digitado.Length >= 2)
            {
                _processandoAutoDesc = true;
                try
                {
                    int pos = digitado.Length;
                    TxtEditDescricao.Text = matchPrefixo.Descricao;
                    TxtEditDescricao.Select(pos, matchPrefixo.Descricao.Length - pos);

                    VincularCamposDaSugestao(matchPrefixo);
                }
                finally
                {
                    _processandoAutoDesc = false;
                }
            }
        }
        else
        {
            PopupAutoCompleteDescricao.IsOpen = false;
        }
    }

    private void ListaSugestoesDescricao_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ListaSugestoesDescricao.SelectedItem is OpcaoSugestaoDescricao sel)
        {
            AplicarSugestaoDescricaoCompleta(sel.Dados, selecionarRestoTexto: false);
            PopupAutoCompleteDescricao.IsOpen = false;
        }
    }

    private void AplicarSugestaoDescricaoCompleta(SugestaoDescricaoHistorico s, bool selecionarRestoTexto)
    {
        _processandoAutoDesc = true;
        try
        {
            TxtEditDescricao.Text = s.Descricao;
            if (!selecionarRestoTexto)
                TxtEditDescricao.CaretIndex = TxtEditDescricao.Text.Length;

            VincularCamposDaSugestao(s);
        }
        finally
        {
            _processandoAutoDesc = false;
        }
    }

    private void VincularCamposDaSugestao(SugestaoDescricaoHistorico s)
    {
        if (!string.IsNullOrWhiteSpace(s.Categoria))
            RecarregarListaCategorias(s.Categoria);

        if (!string.IsNullOrWhiteSpace(s.FormaPagamento))
            ComboFormaPagamento.SelectedItem = s.FormaPagamento;

        if (s.UltimoValor > 0 && string.IsNullOrWhiteSpace(TxtEditValor.Text))
        {
            _formatandoValor = true;
            TxtEditValor.Text = s.UltimoValor.ToString("N2", new CultureInfo("pt-BR"));
            _formatandoValor = false;
        }
    }

    // ABRE O CALENDARIO DE EVENTOS SAZONAIS
    private void BtnAbrirCalendarioSazonal_Click(object sender, RoutedEventArgs e)
    {
        var modal = new JanelaCalendarioSazonal(_cofre) { Owner = this };
        modal.ShowDialog();
        if (modal.HouveAlteracao)
        {
            AtualizarPainelCompleto();
            TxtStatusCofre.Text = "CALENDÁRIO DE EVENTOS SAZONAIS ATUALIZADO NO RADAR!";
        }
    }

    // ABRE A CENTRAL DE CAIXINHAS DE PLANEJAMENTO & ESTRATEGISTA DO MINI-ORDIS
    private void BtnAbrirCaixinhas_Click(object sender, RoutedEventArgs e)
    {
        int mesRef = ComboMesPizza.SelectedIndex >= 0 ? ComboMesPizza.SelectedIndex + 1 : DateTime.Now.Month;
        string anoRef = ComboAnoPizza.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
        string ym = $"{anoRef}-{mesRef:D2}";

        var modal = new JanelaPlanejamentoCaixinhas(_cofre, _advisor, ym) { Owner = this };
        modal.ShowDialog();
        if (modal.HouveAlteracao)
        {
            AtualizarPainelCompleto();
            TxtStatusCofre.Text = "CAIXINHAS E ESTRATÉGIA NEURAL SINCRONIZADAS COM O COFRE!";
        }
    }

    private void ListaAlertasSazonais_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListaAlertasSazonais.SelectedItem is string alerta && alerta.Contains("[CAIXINHA"))
            BtnAbrirCaixinhas_Click(sender, e);
        else
            BtnAbrirCalendarioSazonal_Click(sender, e);
    }

    private void TxtEditValor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TxtEditValor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string texto = (string)e.DataObject.GetData(typeof(string));
            if (!Regex.IsMatch(texto, @"[\d]"))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void TxtEditValor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatandoValor) return;

        _formatandoValor = true;
        try
        {
            string apenasDigitos = Regex.Replace(TxtEditValor.Text ?? "", @"[^\d]", "");

            if (string.IsNullOrEmpty(apenasDigitos) || !long.TryParse(apenasDigitos, out long centavos) || centavos == 0)
            {
                TxtEditValor.Text = string.Empty;
            }
            else
            {
                decimal valorFormatado = centavos / 100m;
                TxtEditValor.Text = valorFormatado.ToString("N2", new CultureInfo("pt-BR"));
                TxtEditValor.CaretIndex = TxtEditValor.Text.Length;
            }
        }
        finally
        {
            _formatandoValor = false;
        }
    }

    private void LimparBarrasEdicao()
    {
        _itemSelecionado = null;
        TxtEditDescricao.Clear();

        _formatandoValor = true;
        TxtEditValor.Clear();
        _formatandoValor = false;

        ComboEditCategoria.SelectedIndex = -1;
        ComboFormaPagamento.SelectedIndex = -1;
        BtnAprovarPrincipal.Content = "+ LANÇAR MANUAL";
    }

    // BOTAO UNIFICADO: Se tiver item selecionado na Quarentena, confirma/abre matriz do PDF; senao, lanca manualmente!
    private void BtnLancamentoOuConfirmar_Click(object sender, RoutedEventArgs e)
    {
        if (_itemSelecionado != null)
        {
            BtnAprovar_Click(sender, e);
        }
        else
        {
            BtnLancamentoManual_Click(sender, e);
        }
    }

    private void BtnLancamentoManual_Click(object sender, RoutedEventArgs e)
    {
        string desc = TxtEditDescricao.Text.Trim();
        decimal.TryParse(TxtEditValor.Text.Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valorManual);

        if (string.IsNullOrWhiteSpace(desc) || valorManual <= 0)
        {
            JanelaMensagem.Exibir("Preencha a Descrição e digite os números do Valor (R$) nas barras acima para lançar!", "Mini-Ordis // Lançamento Manual", false, this);
            return;
        }

        string categoriaFinal = ComboEditCategoria.SelectedItem?.ToString() ?? "Triviais";
        string formaPagto = ComboFormaPagamento.SelectedItem?.ToString() ?? "Pix";

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            INSERT INTO Transacoes (Descricao, Valor, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo)
            VALUES (@Desc, @Val, @Data, 2, @Cat, @Forma, @Parc, 'Manual');",
            new
            {
                Desc = desc,
                Val = valorManual,
                Data = DateTime.Now.ToString("yyyy-MM-dd"),
                Cat = categoriaFinal,
                Forma = formaPagto
            });

        _cofre.AprenderRegraEstabelecimento(desc, categoriaFinal);

        ListaQuarentena.SelectedIndex = -1;
        LimparBarrasEdicao();
        AtualizarPainelCompleto();
        TxtStatusCofre.Text = $"LANÇAMENTO MANUAL '{desc.ToUpper()}' (R$ {valorManual:N2}) GRAVADO NO COFRE!";
    }

        private void BtnSlot1_Click(object sender, RoutedEventArgs e)
    {
        int mesRef = ComboMesPizza.SelectedIndex >= 0 ? ComboMesPizza.SelectedIndex + 1 : DateTime.Now.Month;
        string anoRef = ComboAnoPizza.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();

        var janelaCaixa = new JanelaReceitasCaixa(_cofre, mesRef, anoRef) { Owner = this };
        janelaCaixa.ShowDialog();

        if (janelaCaixa.HouveAlteracao)
        {
            AtualizarPainelCompleto();
            TxtStatusCofre.Text = "CAIXA MENSAL E CONTRACHEQUES SINCRONIZADOS COM O ESCUDO!";
        }
    }

        private void BtnSlot2_Click(object sender, RoutedEventArgs e)
    {
        int mesRef = ComboMesPizza.SelectedIndex >= 0 ? ComboMesPizza.SelectedIndex + 1 : DateTime.Now.Month;
        string anoRef = ComboAnoPizza.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();

        var janelaCofre = new JanelaRegistrosCofre(_cofre, mesRef, anoRef) { Owner = this };
        janelaCofre.ShowDialog();

        if (janelaCofre.HouveAlteracao)
        {
            RecarregarListaCategorias();
            AtualizarPainelCompleto();
            TxtStatusCofre.Text = "REGISTROS DO COFRE E TELEMETRIA ATUALIZADOS COM SUCESSO!";
        }
    }

    private void AtualizarPainelCompleto(bool selecionarPrimeiroDaFila = false)
    {
        _memoriaDescricoes = _cofre.ObterSugestoesDescricao();
        PopupAutoCompleteDescricao.IsOpen = false;
        RecarregarListaCategorias(ComboEditCategoria.SelectedItem?.ToString());

        using var conn = _cofre.CriarConexaoAberta();

        var pendentes = conn.Query<QuarentenaViewModel>("SELECT * FROM Quarentena WHERE Aprovado = 0 ORDER BY Id DESC").ToList();
        ListaQuarentena.ItemsSource = pendentes;

        if (selecionarPrimeiroDaFila && pendentes.Any())
        {
            ListaQuarentena.SelectedIndex = 0;
        }
        else
        {
            ListaQuarentena.SelectedIndex = -1;
            LimparBarrasEdicao();
            TxtPreviewBruto.Text = pendentes.Any()
                ? $"[MINI-ORDIS // AGUARDANDO SELEÇÃO]\n• Há {pendentes.Count} arquivo(s) na fila de Quarentena.\n• Clique em um item acima para carregar os dados nas barras, ou digite diretamente nas barras vazias para um Lançamento Manual."
                : "[MINI-ORDIS // SISTEMA DE QUARENTENA VAZIO]\n• Nenhum arquivo pendente na Quarentena.\n• As barras acima estão livres para Lançamento Manual ou você pode clicar em '+ IMPORTAR ARQUIVO'.";
        }

        int mesEscudo = ComboMesPizza.SelectedIndex >= 0 ? ComboMesPizza.SelectedIndex + 1 : DateTime.Now.Month;
        string anoEscudo = ComboAnoPizza.SelectedItem?.ToString() ?? DateTime.Now.Year.ToString();
        string ymEscudo = $"{anoEscudo}-{mesEscudo:D2}";

        var balancoAtual = _cofre.CalcularBalancoMes(ymEscudo);
        decimal rendaReferencia = balancoAtual.RendaLiquidaReal > 0 ? balancoAtual.RendaLiquidaReal : 5000m;
        decimal metaEscudo10 = Math.Round(rendaReferencia * 0.10m, 2);

        var diag = _advisor.GerarDiagnosticoMensal(rendaReferencia, metaEscudo10, ymEscudo);
        if (!pendentes.Any()) { TxtPreviewBruto.Text = _advisor.GerarPlanoEstrategicoCompleto(ymEscudo); }

        int diasMes = DateTime.DaysInMonth(int.Parse(anoEscudo), mesEscudo);
        int diasRest = (int.Parse(anoEscudo) == DateTime.Now.Year && mesEscudo == DateTime.Now.Month)
            ? Math.Max(1, diasMes - DateTime.Now.Day + 1)
            : diasMes;
        decimal saldoLivreReal = rendaReferencia - balancoAtual.GastosLiquidos - metaEscudo10;
        decimal poderDiario = saldoLivreReal > 0 ? saldoLivreReal / diasRest : 0m;

        string tagOrigemCaixa = balancoAtual.RendaLiquidaReal > 0 ? $"CAIXA REAL: R$ {balancoAtual.RendaLiquidaReal:N2}" : "CAIXA BASE: R$ 5.000,00 (Cadastre em 💰 CAIXA / HOLERITE)";
        TxtTituloEscudo.Text = $"ESCUDO [{ymEscudo}]: {diag.NivelEscudo} // {tagOrigemCaixa} // GASTO: R$ {balancoAtual.GastosLiquidos:N2} ({diag.PercentualCaixaConsumido}%)";
        TxtConselhoOrdis.Text = $"{diag.MensagemConselho} [Saldo Livre: R$ {saldoLivreReal:N2} | Poder de Fogo Diário: R$ {poderDiario:N2}/dia]";
        BarraCaixa.Value = (double)Math.Min(100, diag.PercentualCaixaConsumido);

        BarraCaixa.Foreground = diag.NivelEscudo switch
        {
            "VERMELHO" => new SolidColorBrush(Color.FromRgb(255, 0, 85)),
            "AMARELO" => new SolidColorBrush(Color.FromRgb(255, 184, 0)),
            _ => new SolidColorBrush(Color.FromRgb(0, 255, 102))
        };

        ListaAlertasSazonais.ItemsSource = diag.AlertasSazonais;

        AtualizarGraficosPizza();
        AtualizarGraficoLinhas();
    }

    private void ListaQuarentena_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaQuarentena.SelectedItem is not QuarentenaViewModel item)
        {
            LimparBarrasEdicao();
            return;
        }

        _itemSelecionado = item;
        string textoBruto = item.TextoExtraidoBruto ?? string.Empty;
        bool ehFatura = _faturaParser.EhFaturaCartao(textoBruto, item.CaminhoArquivo);
        bool ehDuplicidadeFatura = _faturaParser.EhComprovantePagamentoFaturaDuplicado(textoBruto);

        string alertaExtra = "";

        if (ehFatura)
        {
            var extracao = _faturaParser.ExtrairFaturaCompleta(item.CaminhoArquivo, textoBruto);

            if (extracao.ValorTotalFatura > 0 && item.ValorSugerido != extracao.ValorTotalFatura)
            {
                item.ValorSugerido = extracao.ValorTotalFatura;
                using var conn = _cofre.CriarConexaoAberta();
                conn.Execute("UPDATE Quarentena SET ValorSugerido = @Val WHERE Id = @Id", new { Val = extracao.ValorTotalFatura, item.Id });
            }

            ComboFormaPagamento.SelectedItem = "Cartão de Crédito";
            BtnAprovarPrincipal.Content = "⚡ MATRIZ 4 CARTÕES";

            int qtdCartoes = extracao.Itens.Select(x => x.CartaoFinal).Distinct().Count();
            int desconhecidos = extracao.Itens.Count(x => !x.ConhecidoPeloOrdis);
            alertaExtra = $"\n• Seção 'Despesas da Fatura': {extracao.Itens.Count} compras em {qtdCartoes} cartões ({desconhecidos} para revisar).";
        }
        else
        {
            ComboFormaPagamento.SelectedItem = "Pix";
            BtnAprovarPrincipal.Content = "+ LANÇAR MANUAL";
            if (ehDuplicidadeFatura)
            {
                alertaExtra = "\n⚠ [ALERTA ANTI-DUPLICIDADE]: Este comprovante parece ser o pagamento de uma fatura! Descarte-o se já desmembrou o PDF!";
            }
        }

        TxtEditDescricao.Text = item.DescricaoSugerida;

        _formatandoValor = true;
        TxtEditValor.Text = item.ValorSugerido > 0 ? item.ValorSugerido.ToString("N2", new CultureInfo("pt-BR")) : string.Empty;
        _formatandoValor = false;

        _cofre.GarantirCategoria(item.CategoriaSugerida, "Mini-Ordis");
        RecarregarListaCategorias(item.CategoriaSugerida);

        DateTime venc = _faturaParser.ExtrairDataVencimento(textoBruto);

        TxtPreviewBruto.Text = 
            $"[MINI-ORDIS // SINTETIZADOR DE DOCUMENTOS]\n" +
            $"• Arquivo Alvo: {Path.GetFileName(item.CaminhoArquivo)}\n" +
            $"• Forma de Pagamento Detectada: {ComboFormaPagamento.SelectedItem}\n" +
            $"• Valor da Fatura / Documento: R$ {item.ValorSugerido:N2}\n" +
            $"• Vencimento / Data Base: {venc:dd/MM/yyyy}" +
            alertaExtra;
    }

        private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] arquivos && arquivos.Any())
        {
            ImportarListaDeArquivos(arquivos);
        }
    }

    private void ImportarListaDeArquivos(IEnumerable<string> caminhos)
    {
        int importados = 0;
        foreach (var caminho in caminhos)
        {
            if (!File.Exists(caminho)) continue;
            var res = _ingestao.ProcessarArquivoParaQuarentena(caminho);
            if (res.IsSuccess)
            {
                _cofre.GarantirCategoria(res.Value.CategoriaSugerida, "Mini-Ordis");
                importados++;
            }
            else
            {
                JanelaMensagem.Exibir(res.Errors[0].Message, "Erro na Ingestao", false, this);
            }
        }

        if (importados > 0)
        {
            AtualizarPainelCompleto(selecionarPrimeiroDaFila: true);
            TxtStatusCofre.Text = $"{importados} ARQUIVO(S) IMPORTADO(S) PELO EXPLORADOR NEURAL!";
        }
    }

    private void BtnImportar_Click(object sender, RoutedEventArgs e)
    {
        var explorador = new JanelaExploradorArquivos { Owner = this };
        if (explorador.ShowDialog() == true && explorador.ArquivosSelecionados.Any())
        {
            ImportarListaDeArquivos(explorador.ArquivosSelecionados);
        }
    }

    private void BtnAprovar_Click(object sender, RoutedEventArgs e)
    {
        if (_itemSelecionado == null) return;

        decimal.TryParse(TxtEditValor.Text.Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valorFinal);
        string categoriaFinal = ComboEditCategoria.SelectedItem?.ToString() ?? "Triviais";
        string formaPagto = ComboFormaPagamento.SelectedItem?.ToString() ?? "Pix";
        string textoBruto = _itemSelecionado.TextoExtraidoBruto ?? string.Empty;

        if (_faturaParser.EhFaturaCartao(textoBruto, _itemSelecionado.CaminhoArquivo) || formaPagto == "Cartão de Crédito")
        {
            var extracao = _faturaParser.ExtrairFaturaCompleta(_itemSelecionado.CaminhoArquivo, textoBruto);
            if (extracao.Itens.Any())
            {
                var modal = new JanelaConciliacao(
                    _cofre,
                    extracao,
                    valorFinal,
                    _itemSelecionado.Id,
                    Path.GetExtension(_itemSelecionado.CaminhoArquivo))
                {
                    Owner = this
                };

                if (modal.ShowDialog() == true)
                {
                    LimparBarrasEdicao();
                    RecarregarListaCategorias();
                    AtualizarPainelCompleto();
                    TxtStatusCofre.Text = "OS 4 CARTÕES FORAM DESMEMBRADOS E CONSOLIDADOS NO COFRE!";
                }
                return;
            }
        }

        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute(@"
            INSERT INTO Transacoes (Descricao, Valor, Data, Tipo, Categoria, FormaPagamento, ParcelaInfo, OrigemArquivo)
            VALUES (@Desc, @Val, @Data, 2, @Cat, @Forma, '', @Origem);
            UPDATE Quarentena SET Aprovado = 1 WHERE Id = @Id;",
            new
            {
                Desc = string.IsNullOrWhiteSpace(TxtEditDescricao.Text) ? "Lancamento" : TxtEditDescricao.Text,
                Val = valorFinal,
                Data = DateTime.Now.ToString("yyyy-MM-dd"),
                Cat = categoriaFinal,
                Forma = formaPagto,
                Origem = Path.GetExtension(_itemSelecionado.CaminhoArquivo),
                Id = _itemSelecionado.Id
            });

        _cofre.AprenderRegraEstabelecimento(TxtEditDescricao.Text, categoriaFinal);
        LimparBarrasEdicao();
        AtualizarPainelCompleto();
    }

    private void BtnDescartar_Click(object sender, RoutedEventArgs e)
    {
        if (_itemSelecionado == null) return;
        using var conn = _cofre.CriarConexaoAberta();
        conn.Execute("DELETE FROM Quarentena WHERE Id = @Id", new { _itemSelecionado.Id });
        LimparBarrasEdicao();
        AtualizarPainelCompleto();
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        var res = _cofre.RealizarBackupRapido(_pastaBackups);
        if (res.IsSuccess)
            TxtStatusCofre.Text = $"CÁPSULA .ORDISBAK (DADOS + LOGIN + 3 PALAVRAS) SALVA ÀS {DateTime.Now:HH:mm:ss}!";
        else
            JanelaMensagem.Exibir(res.Errors[0].Message, "Mini-Ordis // Alerta", false, this);
    }
}


















