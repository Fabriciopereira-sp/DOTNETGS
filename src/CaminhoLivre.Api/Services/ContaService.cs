using CaminhoLivre.Api.Data;
using CaminhoLivre.Api.Domain;
using CaminhoLivre.Api.Infra;
using Microsoft.EntityFrameworkCore;

namespace CaminhoLivre.Api.Services;

// Exclusão dos dados do próprio usuário (LGPD), no mesmo espírito do DELETE /api/v1/auth/eu da API Java:
// apaga os reportes dele (com histórico e fotos), os registros de idempotência e o cadastro local, e guarda o ID
// em VV_CONTA_EXCLUIDA para o token deixar de valer aqui na hora.
public class ContaService(AppDbContext db, IArmazenamentoDeFotos armazenamento, TimeProvider relogio)
{
    public Task<bool> FoiExcluidaAsync(long usuarioId) =>
        db.ContasExcluidas.AsNoTracking().AnyAsync(c => c.UsuarioId == usuarioId);

    public async Task ExcluirAsync(long usuarioId, CancellationToken cancellationToken)
    {
        var reportes = await db.Reportes.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId)
            .Select(r => new { r.Id, r.FotoUrl })
            .ToListAsync(cancellationToken);
        var ids = reportes.Select(r => r.Id).ToList();

        await using (var transacao = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.HistoricosStatus.Where(h => ids.Contains(h.ReporteId)).ExecuteDeleteAsync(cancellationToken);
            await db.Reportes.Where(r => r.UsuarioId == usuarioId).ExecuteDeleteAsync(cancellationToken);
            await db.RegistrosIdempotencia.Where(i => i.UsuarioId == usuarioId).ExecuteDeleteAsync(cancellationToken);
            await db.Usuarios.Where(u => u.Id == usuarioId).ExecuteDeleteAsync(cancellationToken);

            if (!await db.ContasExcluidas.AnyAsync(c => c.UsuarioId == usuarioId, cancellationToken))
            {
                db.ContasExcluidas.Add(new ContaExcluida { UsuarioId = usuarioId, ExcluidaEm = relogio.GetUtcNow().UtcDateTime });
                await db.SaveChangesAsync(cancellationToken);
            }

            await transacao.CommitAsync(cancellationToken);
        }

        // As fotos saem depois do commit: se o banco falhar, nada some do disco.
        foreach (var fotoUrl in reportes.Select(r => r.FotoUrl).OfType<string>())
        {
            await armazenamento.ExcluirAsync(fotoUrl, cancellationToken);
        }
    }
}
