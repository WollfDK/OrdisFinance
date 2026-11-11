using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public partial class JanelaNovaCategoria : Window
{
    private readonly CofreSqliteService _cofre;
    public string CategoriaCriada { get; private set; } = string.Empty;
    public bool HouveAlteracao { get; private set; } = false;

    public JanelaNovaCategoria(CofreSqliteService cofre)
    {
        InitializeComponent();
        _cofre = cofre;
        RecarregarCategoriasNaTela();
    }

    private void BarraTitulo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void RecarregarCategoriasNaTela(string? selecionar = null)
    {
        var lista = _cofre.ObterCategorias();
        ListaCategorias.ItemsSource = lista;

        if (!string.IsNullOrWhiteSpace(selecionar))
        {
            var match = lista.FirstOrDefault(c => c.Equals(selecionar, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ListaCategorias.SelectedItem = match;
                return;
            }
        }

        if (lista.Any())
            ListaCategorias.SelectedIndex = 0;
    }

    private void ListaCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaCategorias.SelectedItem is not string selecionada)
        {
            TxtAlterarNome.Clear();
            TxtImpactoAlterar.Text = "Nenhuma categoria selecionada.";
            ComboCategoriaSubstituta.ItemsSource = null;
            return;
        }

        TxtAlterarNome.Text = selecionada;

        var impacto = _cofre.ContarImpactoCategoria(selecionada);
        TxtImpactoAlterar.Text = $"Vinculados a '{selecionada}': {impacto.QtdTransacoes} lançamento(s) no Cofre e {impacto.QtdRegras} regra(s) aprendida(s).";

        var outrasCategorias = _cofre.ObterCategorias()
            .Where(c => !c.Equals(selecionada, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ComboCategoriaSubstituta.ItemsSource = outrasCategorias;
        if (outrasCategorias.Any())
            ComboCategoriaSubstituta.SelectedIndex = 0;
    }

    private void BtnCriar_Click(object sender, RoutedEventArgs e)
    {
        string nova = TxtCriarNome.Text.Trim();
        if (string.IsNullOrWhiteSpace(nova))
        {
            JanelaMensagem.Exibir("Digite um nome para criar a nova categoria, Operador!", "Mini-Ordis // Nova Categoria", false, this);
            return;
        }

        if (nova.Equals("Cartão", StringComparison.OrdinalIgnoreCase) || nova.Equals("Cartao", StringComparison.OrdinalIgnoreCase))
        {
            JanelaMensagem.Exibir("A diretriz do Operador proíbe 'Cartão' como categoria! Use a Forma de Pagamento.", "Mini-Ordis // Diretriz Ativa", false, this);
            return;
        }

        _cofre.GarantirCategoria(nova, "Operador");
        TxtCriarNome.Clear();
        CategoriaCriada = nova;
        HouveAlteracao = true;

        RecarregarCategoriasNaTela(nova);
        TxtFeedback.Text = $"✔ Categoria '{nova}' criada com sucesso no Cofre!";
    }

    private void BtnAlterar_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCategorias.SelectedItem is not string antiga)
        {
            JanelaMensagem.Exibir("Selecione uma categoria na lista à esquerda para alterar!", "Mini-Ordis", false, this);
            return;
        }

        string novoNome = TxtAlterarNome.Text.Trim();
        if (string.IsNullOrWhiteSpace(novoNome))
        {
            JanelaMensagem.Exibir("Digite o novo nome para a categoria!", "Mini-Ordis", false, this);
            return;
        }

        var impacto = _cofre.ContarImpactoCategoria(antiga);
        var res = _cofre.RenomearCategoriaEmCascata(antiga, novoNome);

        if (res.IsSuccess)
        {
            CategoriaCriada = novoNome;
            HouveAlteracao = true;
            RecarregarCategoriasNaTela(novoNome);
            TxtFeedback.Text = $"✔ '{antiga}' alterada para '{novoNome}' ({impacto.QtdTransacoes} lançamentos atualizados automaticamente)!";
        }
        else
        {
            JanelaMensagem.Exibir(res.Errors[0].Message, "Erro ao Alterar Categoria", false, this);
        }
    }

    private void BtnRemover_Click(object sender, RoutedEventArgs e)
    {
        if (ListaCategorias.SelectedItem is not string categoriaRemover)
        {
            JanelaMensagem.Exibir("Selecione na lista à esquerda a categoria que deseja remover!", "Mini-Ordis", false, this);
            return;
        }

        if (ComboCategoriaSubstituta.SelectedItem is not string categoriaDestino || string.IsNullOrWhiteSpace(categoriaDestino))
        {
            JanelaMensagem.Exibir("É necessário existir e selecionar outra categoria na caixa ao lado para receber os lançamentos!", "Mini-Ordis // Segurança", false, this);
            return;
        }

        var impacto = _cofre.ContarImpactoCategoria(categoriaRemover);

        string textoAviso =
            $"⚠ AVISO DE MIGRAÇÃO AUTOMÁTICA DO MINI-ORDIS:\n\n" +
            $"Você solicitou a remoção da categoria '{categoriaRemover}'.\n" +
            $"• Lançamentos no Cofre que serão migrados: {impacto.QtdTransacoes}\n" +
            $"• Itens na Quarentena que serão migrados: {impacto.QtdQuarentena}\n" +
            $"• Regras de memória que serão migradas: {impacto.QtdRegras}\n\n" +
            $"Deseja transferir TODOS os itens de '{categoriaRemover}' automaticamente para '{categoriaDestino}' e excluir '{categoriaRemover}'?";

        bool confirmado = JanelaMensagem.Exibir(
            textoAviso,
            "CONFIRMAR REMOÇÃO E TRANSFERÊNCIA EM CASCATA",
            confirmarSimNao: true,
            owner: this);

        if (!confirmado)
            return;

        var res = _cofre.RemoverCategoriaComMigracao(categoriaRemover, categoriaDestino);
        if (res.IsSuccess)
        {
            CategoriaCriada = categoriaDestino;
            HouveAlteracao = true;
            RecarregarCategoriasNaTela(categoriaDestino);
            TxtFeedback.Text = $"✔ '{categoriaRemover}' removida! Todos os itens foram transferidos para '{categoriaDestino}'.";
        }
        else
        {
            JanelaMensagem.Exibir(res.Errors[0].Message, "Erro ao Remover Categoria", false, this);
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = HouveAlteracao;
        Close();
    }
}
