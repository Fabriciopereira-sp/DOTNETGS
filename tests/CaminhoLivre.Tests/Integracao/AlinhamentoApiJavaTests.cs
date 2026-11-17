using System.Net;
using System.Net.Http.Json;
using System.Text;
using CaminhoLivre.Api.Contracts;
using CaminhoLivre.Api.Data;
using CaminhoLivre.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CaminhoLivre.Tests.Integracao;

// Regras que precisam bater com a API Java: papéis que ela emite, órgão do gestor e exclusão de conta (LGPD).
[Collection(ColecaoIntegracao.Nome)]
public class AlinhamentoApiJavaTests(ApiFactory api)
{
    [Fact]
    public async Task Cuidador_CriaEAcompanhaOProprioReporte()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Cuidador);

        var criado = await cliente.CriarReporteComSucessoAsync("RAMPA");
        var meus = await cliente.GetFromJsonAsync<PaginaResponse<ReporteResponse>>("/api/reportes/meus");
        var detalhe = await cliente.GetAsync($"/api/reportes/{criado.Id}");

        Assert.Equal(1, meus!.Total);
        Assert.Equal(HttpStatusCode.OK, detalhe.StatusCode);
    }

    [Fact]
    public async Task ExcluirConta_ApagaOsReportesEOTokenDeixaDeValer()
    {
        var usuario = await api.CriarUsuarioAsync(Papeis.Cidadao);
        var cliente = api.ClienteComToken(api.GerarToken(usuario));
        await cliente.CriarReporteComSucessoAsync();
        await cliente.CriarReporteComSucessoAsync("LIXO");

        var exclusao = await cliente.DeleteAsync("/api/usuarios/eu");
        var depois = await cliente.GetAsync("/api/reportes/meus");

        Assert.Equal(HttpStatusCode.NoContent, exclusao.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Reportes.AnyAsync(r => r.UsuarioId == usuario.Id));
        Assert.False(await db.RegistrosIdempotencia.AnyAsync(i => i.UsuarioId == usuario.Id));
        Assert.False(await db.Usuarios.AnyAsync(u => u.Id == usuario.Id));
    }

    [Fact]
    public async Task ExcluirConta_UsuarioQueNuncaUsouEstaApi_Retorna204()
    {
        var cliente = api.ClienteComToken(api.GerarToken(new Usuario
        {
            Id = Random.Shared.NextInt64(10_000_000, 9_000_000_000),
            Nome = "Só existe no Java",
            Role = Papeis.Cidadao
        }));

        var exclusao = await cliente.DeleteAsync("/api/usuarios/eu");
        var depois = await cliente.GetAsync("/api/categorias");

        Assert.Equal(HttpStatusCode.NoContent, exclusao.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
    }

    [Fact]
    public async Task ExcluirConta_Gestor_Retorna403()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Gestor, await api.ObterOrgaoIdAsync(AreaOrgao.OBRAS));

        var resposta = await cliente.DeleteAsync("/api/usuarios/eu");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // Token igual ao do login da API Java: o gestor nunca usou esta API e o órgão vem na claim "orgao".
    private static string TokenDoJava(string role, string? orgao)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Random.Shared.NextInt64(10_000_000, 9_000_000_000).ToString(),
            ["name"] = "Gestor do Java",
            ["role"] = role
        };
        if (orgao is not null)
        {
            claims["orgao"] = orgao;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "voz-e-via-java",
            Audience = "voz-e-via",
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.ChaveJwtDeTeste)), SecurityAlgorithms.HmacSha256)
        });
    }

    [Fact]
    public async Task GestorDoJava_UsaOOrgaoDoTokenParaVerAFilaEOReporte()
    {
        var cidadao = await api.ClienteAutenticadoAsync(Papeis.Cidadao);
        var reporte = await cidadao.CriarReporteComSucessoAsync("ILUMINACAO");
        var gestorIluminacao = api.ClienteComToken(TokenDoJava(Papeis.Gestor, "ILUMINACAO"));
        var gestorObras = api.ClienteComToken(TokenDoJava(Papeis.Gestor, "OBRAS"));

        var fila = await gestorIluminacao.GetFromJsonAsync<PaginaResponse<ReporteResponse>>("/api/gestor/reportes?size=50");
        var detalheDoOrgao = await gestorIluminacao.GetAsync($"/api/reportes/{reporte.Id}");
        var detalheDeOutroOrgao = await gestorObras.GetAsync($"/api/reportes/{reporte.Id}");

        Assert.Contains(fila!.Itens, r => r.Id == reporte.Id);
        Assert.Equal(HttpStatusCode.OK, detalheDoOrgao.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detalheDeOutroOrgao.StatusCode);
    }

    [Fact]
    public async Task GestorSemOrgao_Retorna403NaFila()
    {
        var gestor = api.ClienteComToken(TokenDoJava(Papeis.Gestor, null));

        var resposta = await gestor.GetAsync("/api/gestor/reportes");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }
}
