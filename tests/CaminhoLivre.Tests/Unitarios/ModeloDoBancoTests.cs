using CaminhoLivre.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CaminhoLivre.Tests.Unitarios;

// Os testes de integração usam SQLite, que aceita gravar um ID informado mesmo em coluna autoincremento.
// Este teste olha o DDL que o EF Core gera para o SQL Server (Azure SQL), onde isso não vale.
public class ModeloDoBancoTests
{
    private static string DdlSqlServer()
    {
        var opcoes = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=nao-conecta;Database=nao-conecta")
            .Options;
        using var db = new AppDbContext(opcoes);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void IdDoUsuario_NaoEIdentity_PorqueVemDaApiJava()
    {
        var ddl = DdlSqlServer();
        var tabela = ddl[ddl.IndexOf("CREATE TABLE [VV_USUARIO]", StringComparison.Ordinal)..];
        var primeiraColuna = tabela.Split('\n')[1];

        Assert.Contains("[ID] bigint NOT NULL", primeiraColuna);
        Assert.DoesNotContain("IDENTITY", primeiraColuna);
    }

    [Fact]
    public void TabelaDeContasExcluidas_Existe()
    {
        Assert.Contains("CREATE TABLE [VV_CONTA_EXCLUIDA]", DdlSqlServer());
    }
}
