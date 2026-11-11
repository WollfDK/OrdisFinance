using System.IO;
using System.Windows;
using Dapper;
using OrdisFinance.Infrastructure;

namespace OrdisFinance.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string pastaBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CofreDados");
        string arquivoDb = Path.Combine(pastaBase, "OrdisFinance.db");
        string pastaBackups = Path.Combine(pastaBase, "Backups");

        var cofre = new CofreSqliteService(arquivoDb, "Operador#Cyber2077");
        var resultado = cofre.InicializarCofre();

        if (resultado.IsSuccess)
        {
            using var conn = cofre.CriarConexaoAberta();
            var total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM EventosSazonais");
            if (total == 0)
            {
                conn.Execute(@"INSERT INTO EventosSazonais (Titulo, ValorAlvo, DataVencimento, MesesAntecedenciaAlerta) 
                               VALUES ('IPVA e Licenciamento Fox', 650.00, '2027-04-15', 4),
                                      ('Meta Escudo Mensal (10%)', 520.00, '2026-12-05', 1);");
            }

            cofre.RealizarBackupRapido(pastaBackups);
        }
        else
        {
            JanelaMensagem.Exibir(resultado.Errors[0].Message, "Alerta Vermelho do Ordis!");
        }
    }
}
