using System.Security.Claims;
using CaminhoLivre.Api.Auth;
using CaminhoLivre.Api.Services;

namespace CaminhoLivre.Api.Endpoints;

public static class ContaEndpoints
{
    public static IEndpointRouteBuilder MapContaEndpoints(this IEndpointRouteBuilder app)
    {
        // O app chama este endpoint antes do DELETE /api/v1/auth/eu da API Java, com o mesmo token.
        app.MapDelete("/api/usuarios/eu", async (ClaimsPrincipal user, ContaService contas, CancellationToken cancellationToken) =>
            {
                await contas.ExcluirAsync(user.ObterUsuarioId(), cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization(Politicas.Solicitante)
            .WithTags("Conta (LGPD)")
            .WithName("ExcluirMeusDados")
            .WithSummary("Exclui os dados do usuário logado nesta API (reportes, histórico, fotos e cadastro). Depois disso o token deixa de valer aqui")
            .WithOpenApi()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
