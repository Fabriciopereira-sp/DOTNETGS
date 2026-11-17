using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaminhoLivre.Api.Infra;

// O reporte tem controle de concorrência (AtualizadoEm). Se duas pessoas alteram o mesmo reporte ao mesmo
// tempo (ex.: o gestor muda o status enquanto o cidadão cancela), quem gravar por último recebe 409 em vez
// de sobrescrever a mudança do outro.
public class ConflitoDeConcorrenciaHandler(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excecao, CancellationToken cancellationToken)
    {
        if (excecao is not DbUpdateConcurrencyException)
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = excecao,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Reporte alterado por outra pessoa",
                Detail = "O reporte mudou enquanto você o alterava. Abra de novo e tente outra vez."
            }
        });
    }
}
