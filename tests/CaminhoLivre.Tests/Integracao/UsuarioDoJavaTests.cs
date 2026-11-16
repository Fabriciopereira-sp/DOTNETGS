using System.Net;
using System.Net.Http.Json;
using CaminhoLivre.Api.Contracts;
using CaminhoLivre.Api.Domain;

namespace CaminhoLivre.Tests.Integracao;

// Usuário cadastrado na API Java que ainda não existe na base da API .NET.
[Collection(ColecaoIntegracao.Nome)]
public class UsuarioDoJavaTests(ApiFactory api)
{
    private static Usuario UsuarioQueSoExisteNoJava() => new()
    {
        Id = Random.Shared.NextInt64(1_000_000, 9_000_000),
        Nome = "Cidadã cadastrada no Java",
        Email = "nao-salvo@teste.com",
        Role = Papeis.Cidadao
    };

    [Fact]
    public async Task TokenValidoDeUsuarioNovo_CriaReporteERegistraOUsuario()
    {
        var cliente = api.ClienteComToken(api.GerarToken(UsuarioQueSoExisteNoJava()));

        var criacao = await cliente.CriarReporteAsync(Requisicoes.NovoReporte());
        var meus = await cliente.GetFromJsonAsync<PaginaResponse<ReporteResponse>>("/api/reportes/meus");

        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        Assert.Equal(1, meus!.Total);
    }

    [Fact]
    public async Task UsuarioNovo_SegundoReporteUsaOMesmoCadastro()
    {
        var cliente = api.ClienteComToken(api.GerarToken(UsuarioQueSoExisteNoJava()));

        var primeiro = await cliente.CriarReporteAsync(Requisicoes.NovoReporte());
        var segundo = await cliente.CriarReporteAsync(Requisicoes.NovoReporte());

        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segundo.StatusCode);
    }
}
