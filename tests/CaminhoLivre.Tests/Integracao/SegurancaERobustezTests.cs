using System.Net;
using System.Net.Http.Json;
using CaminhoLivre.Api.Contracts;
using CaminhoLivre.Api.Data;
using CaminhoLivre.Api.Domain;
using CaminhoLivre.Api.Infra;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaminhoLivre.Tests.Integracao;

// Regras de segurança e de consistência: categorias por órgão, foto de um reporte só, limites de upload e
// alteração simultânea do mesmo reporte.
[Collection(ColecaoIntegracao.Nome)]
public class SegurancaERobustezTests(ApiFactory api)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static MultipartFormDataContent Arquivo(byte[] conteudo) =>
        new() { { new ByteArrayContent(conteudo), "arquivo", "foto.png" } };

    private static string CodigoNovo() =>
        "T_" + new string(Guid.NewGuid().ToString("N").Where(char.IsLetter).Take(12).ToArray()).ToUpperInvariant();

    private async Task<string> EnviarFotoAsync(HttpClient cliente)
    {
        var envio = await cliente.PostAsync("/api/fotos", Arquivo(Png));
        envio.EnsureSuccessStatusCode();
        return (await envio.Content.ReadFromJsonAsync<FotoResponse>())!.FotoUrl;
    }

    [Fact]
    public async Task Categoria_GestorDeOutroOrgaoNaoCriaNemAlteraNemExclui()
    {
        var obras = await api.ObterOrgaoIdAsync(AreaOrgao.OBRAS);
        var iluminacao = await api.ObterOrgaoIdAsync(AreaOrgao.ILUMINACAO);
        var gestorObras = await api.ClienteAutenticadoAsync(Papeis.Gestor, obras);
        var gestorIluminacao = await api.ClienteAutenticadoAsync(Papeis.Gestor, iluminacao);
        var criacao = await gestorObras.PostAsJsonAsync("/api/categorias", new CategoriaRequest(CodigoNovo(), "Categoria de obras", obras));
        var categoria = (await criacao.Content.ReadFromJsonAsync<CategoriaResponse>())!;

        var criarParaOutroOrgao = await gestorIluminacao.PostAsJsonAsync("/api/categorias", new CategoriaRequest(CodigoNovo(), "Invasão", obras));
        var alterarDeOutroOrgao = await gestorIluminacao.PutAsJsonAsync($"/api/categorias/{categoria.Id}",
            new CategoriaRequest(categoria.Codigo, "Tomada", iluminacao));
        var passarParaOutroOrgao = await gestorObras.PutAsJsonAsync($"/api/categorias/{categoria.Id}",
            new CategoriaRequest(categoria.Codigo, "Empurrada", iluminacao));
        var excluirDeOutroOrgao = await gestorIluminacao.DeleteAsync($"/api/categorias/{categoria.Id}");
        var depois = await gestorObras.GetFromJsonAsync<CategoriaResponse>($"/api/categorias/{categoria.Id}");

        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, criarParaOutroOrgao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, alterarDeOutroOrgao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, passarParaOutroOrgao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, excluirDeOutroOrgao.StatusCode);
        Assert.Equal("Categoria de obras", depois!.Nome);
        Assert.Equal(obras, depois.OrgaoId);
    }

    [Fact]
    public async Task Foto_SoPodeEstarEmUmReporte_MasOReenvioIdempotenteFunciona()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Cidadao);
        var fotoUrl = await EnviarFotoAsync(cliente);
        var comFoto = Requisicoes.NovoReporte() with { FotoUrl = fotoUrl };
        var chave = Guid.NewGuid().ToString();

        var primeiro = await cliente.CriarReporteAsync(comFoto, chave);
        var reenvio = await cliente.CriarReporteAsync(comFoto, chave);
        var outroComAMesmaFoto = await cliente.CriarReporteAsync(comFoto);
        var outro = await cliente.CriarReporteComSucessoAsync();
        var editarParaAMesmaFoto = await cliente.PutAsJsonAsync($"/api/reportes/{outro.Id}",
            new AtualizarReporteRequest("Descrição atualizada do reporte", null, fotoUrl));

        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reenvio.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, outroComAMesmaFoto.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, editarParaAMesmaFoto.StatusCode);
    }

    [Fact]
    public async Task Foto_AcimaDoLimiteNaoEAceita()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Cidadao);
        var grande = new byte[6 * 1024 * 1024];
        Png.CopyTo(grande, 0);

        var resposta = await cliente.PostAsync("/api/fotos", Arquivo(grande));

        Assert.False(resposta.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Foto_LimiteDeEnviosPorMinuto()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Cidadao);

        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            respostas.Add((await cliente.PostAsync("/api/fotos", Arquivo(Png))).StatusCode);
        }

        Assert.All(respostas.Take(10), s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, respostas[10]);
    }

    [Fact]
    public async Task Foto_ServidaSemPermitirQueONavegadorTroqueOTipo()
    {
        var fotoUrl = await EnviarFotoAsync(await api.ClienteAutenticadoAsync(Papeis.Cidadao));

        var resposta = await api.CreateClient().GetAsync(fotoUrl);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", resposta.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Reporte_AlteracaoComDadoDesatualizado_FalhaEmVezDeSobrescrever()
    {
        var cliente = await api.ClienteAutenticadoAsync(Papeis.Cidadao);
        var criado = await cliente.CriarReporteComSucessoAsync();

        using var escopo = api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var lidoAntes = await db.Reportes.FirstAsync(r => r.Id == criado.Id);

        // Enquanto isso, o cidadão cancela o reporte pela API.
        var cancelamento = await cliente.DeleteAsync($"/api/reportes/{criado.Id}");

        lidoAntes.Status = StatusReporte.EM_ANALISE;
        lidoAntes.AtualizadoEm = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.NoContent, cancelamento.StatusCode);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConflitoDeConcorrencia_ViraRespostaHttp409()
    {
        using var escopo = api.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = escopo.ServiceProvider };
        http.Response.Body = new MemoryStream();
        var handler = ActivatorUtilities.CreateInstance<ConflitoDeConcorrenciaHandler>(escopo.ServiceProvider);

        var tratado = await handler.TryHandleAsync(http, new DbUpdateConcurrencyException(), CancellationToken.None);
        var naoTratado = await handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException(), CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.False(naoTratado);
    }
}
