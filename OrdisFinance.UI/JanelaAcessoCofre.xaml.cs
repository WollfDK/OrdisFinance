using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public partial class JanelaAcessoCofre : Window
{
    private readonly IdentidadeOperadorService _identidade;
    private readonly DispatcherTimer _timerRelogio;
    private readonly bool _modoInspecaoEasterEgg;

    public JanelaAcessoCofre(IdentidadeOperadorService identidade, bool modoInspecaoEasterEgg = false)
    {
        InitializeComponent();
        _identidade = identidade;
        _modoInspecaoEasterEgg = modoInspecaoEasterEgg;

        _timerRelogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerRelogio.Tick += (_, _) => TxtRelogioLogin.Text = DateTime.Now.ToString("dd/MM/yyyy // HH:mm:ss");
        TxtRelogioLogin.Text = DateTime.Now.ToString("dd/MM/yyyy // HH:mm:ss");
        _timerRelogio.Start();

        ConfigurarModoInicial();
    }

    private void ConfigurarModoInicial()
    {
        if (!_identidade.EstaInstalado)
        {
            string codigoSorteado = IdentidadeOperadorService.GerarMatriculaCofreAleatoria();
            TxtInstCodigoCofre.Text = $"COFRE {codigoSorteado}";
            TxtMatriculaDestaque.Text = $"COFRE {codigoSorteado} // PRIMEIRA INSTALAÇÃO DETECTADA";
            PainelInstalacao.Visibility = Visibility.Visible;
            PainelLogin.Visibility = Visibility.Collapsed;
            PainelRecuperacao.Visibility = Visibility.Collapsed;
            TxtInstNomeOperador.Focus();
        }
        else if (_modoInspecaoEasterEgg)
        {
            var id = _identidade.IdentidadeAtual!;
            TxtMatriculaDestaque.Text = $"COFRE {id.CodigoCofre} // OPERADOR {id.NomeOperador.ToUpper()}";
            PainelInstalacao.Visibility = Visibility.Collapsed;
            PainelLogin.Visibility = Visibility.Collapsed;
            PainelRecuperacao.Visibility = Visibility.Visible;

            TxtTituloRecuperacao.Text = $"⚡ FICHA TÉCNICA SECRETA // COFRE {id.CodigoCofre}";
            TxtInfoEasterEgg.Text = $"Cofre forjado em {id.DataInstalacao:dd/MM/yyyy 'às' HH:mm:ss}. Para alterar seu nome de Operador ou redefinir a senha, informe as 3 palavras-chave de instalação:";
            TxtRecNovoNome.Text = id.NomeOperador;
        }
        else
        {
            var id = _identidade.IdentidadeAtual!;
            TxtMatriculaDestaque.Text = $"COFRE {id.CodigoCofre}: TRANCADO // OPERADOR {id.NomeOperador.ToUpper()}";
            TxtSaudacaoLogin.Text = $"BEM-VINDO DE VOLTA, OPERADOR {id.NomeOperador.ToUpper()}";
            PainelInstalacao.Visibility = Visibility.Collapsed;
            PainelLogin.Visibility = Visibility.Visible;
            PainelRecuperacao.Visibility = Visibility.Collapsed;
            PwdLoginSenha.Focus();
        }
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // BOTAO DE IMPORTAR CAPSULA DE BACKUP COMPLETO (.ORDISBAK OU .DB)
    private void BtnImportarBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Cápsula de Backup do Ordis (.ordisbak ou .db)",
            Filter = "Cápsula Completa Ordis (*.ordisbak;*.db)|*.ordisbak;*.db|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var resultado = CofreSqliteService.RestaurarCapsulaBackup(dialog.FileName, _identidade.PastaCofre);
        if (resultado.IsSuccess)
        {
            _identidade.Carregar();
            ConfigurarModoInicial();
            TxtStatusSeguranca.Text = "✔ Backup e Identidade restaurados! Digite sua senha para entrar.";
            JanelaMensagem.Exibir(
                $"{resultado.Value}\n\n" +
                (_identidade.IdentidadeAtual != null
                    ? $"• Cofre Restaurado: COFRE {_identidade.IdentidadeAtual.CodigoCofre}\n• Operador: {_identidade.IdentidadeAtual.NomeOperador}\n\nAgora basta digitar a sua senha original para destrancar!"
                    : "Agora conclua seu acesso na tela."),
                "Mini-Ordis // Restauração Concluída",
                false,
                this);
        }
        else
        {
            JanelaMensagem.Exibir(resultado.Errors[0].Message, "Mini-Ordis // Erro ao Importar Backup", false, this);
        }
    }

    private void BtnConcluirInstalacao_Click(object sender, RoutedEventArgs e)
    {
        string nome = TxtInstNomeOperador.Text.Trim();
        string senha = PwdInstSenha.Password.Trim();
        string conf = PwdInstConfirmar.Password.Trim();
        string p1 = TxtInstPalavra1.Text.Trim();
        string p2 = TxtInstPalavra2.Text.Trim();
        string p3 = TxtInstPalavra3.Text.Trim();

        if (string.IsNullOrWhiteSpace(nome))
        {
            JanelaMensagem.Exibir("Escolha o seu Nome de Operador (ou clique em 'Importar Backup' no topo direito)!", "Mini-Ordis // Instalação", false, this);
            return;
        }

        if (string.IsNullOrWhiteSpace(senha) || senha.Length < 3)
        {
            JanelaMensagem.Exibir("Defina uma senha de acesso com pelo menos 3 caracteres!", "Mini-Ordis // Instalação", false, this);
            return;
        }

        if (senha != conf)
        {
            JanelaMensagem.Exibir("A confirmação de senha não confere com a senha digitada!", "Mini-Ordis // Instalação", false, this);
            return;
        }

        if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2) || string.IsNullOrWhiteSpace(p3))
        {
            JanelaMensagem.Exibir("Preencha todas as 3 Palavras-Chave de recuperação! Elas são o seu seguro caso esqueça a senha.", "Mini-Ordis // 3 Palavras-Chave", false, this);
            return;
        }

        string codigoLimpo = TxtInstCodigoCofre.Text.Replace("COFRE ", "").Trim();
        _identidade.RegistrarInstalacaoInicial(codigoLimpo, nome, senha, p1, p2, p3);

        _timerRelogio.Stop();
        DialogResult = true;
    }

    private void PwdLoginSenha_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnEntrarLogin_Click(sender, e);
    }

    private void BtnEntrarLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_identidade.ValidarSenha(PwdLoginSenha.Password))
        {
            _timerRelogio.Stop();
            DialogResult = true;
        }
        else
        {
            JanelaMensagem.Exibir("Senha incorreta! Tente novamente, use suas 3 palavras-chave ou importe um backup.", "Mini-Ordis // Acesso Negado", false, this);
            PwdLoginSenha.Clear();
            PwdLoginSenha.Focus();
        }
    }

    private void BtnIrParaRecuperacao_Click(object sender, RoutedEventArgs e)
    {
        PainelLogin.Visibility = Visibility.Collapsed;
        PainelRecuperacao.Visibility = Visibility.Visible;
        TxtRecNovoNome.Text = _identidade.IdentidadeAtual?.NomeOperador ?? "";
        TxtRecPalavra1.Focus();
    }

    private void BtnVoltarParaLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_modoInspecaoEasterEgg)
        {
            _timerRelogio.Stop();
            DialogResult = false;
            return;
        }

        PainelRecuperacao.Visibility = Visibility.Collapsed;
        PainelLogin.Visibility = Visibility.Visible;
        PwdLoginSenha.Focus();
    }

    private void BtnConfirmarRecuperacao_Click(object sender, RoutedEventArgs e)
    {
        string p1 = TxtRecPalavra1.Text.Trim();
        string p2 = TxtRecPalavra2.Text.Trim();
        string p3 = TxtRecPalavra3.Text.Trim();
        string novoNome = TxtRecNovoNome.Text.Trim();
        string novaSenha = PwdRecNovaSenha.Password.Trim();

        if (!_identidade.ValidarPalavrasChave(p1, p2, p3))
        {
            JanelaMensagem.Exibir("As 3 Palavras-Chave informadas não conferem com o registro de instalação do Cofre!", "Mini-Ordis // Palavras Inválidas", false, this);
            return;
        }

        if (string.IsNullOrWhiteSpace(novaSenha) || novaSenha.Length < 3)
        {
            JanelaMensagem.Exibir("Digite uma nova senha válida (mínimo de 3 caracteres)!", "Mini-Ordis // Nova Senha", false, this);
            return;
        }

        _identidade.RedefinirCredenciais(novoNome, novaSenha);
        JanelaMensagem.Exibir("✔ Credenciais redefinidas com sucesso pelas 3 palavras-chave! Acesso liberado.", "Mini-Ordis // Cofre Destrancado", false, this);

        _timerRelogio.Stop();
        DialogResult = true;
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        _timerRelogio.Stop();
        DialogResult = false;
    }
}
