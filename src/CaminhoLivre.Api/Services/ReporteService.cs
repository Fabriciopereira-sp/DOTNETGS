using System.Security.Claims;
using CaminhoLivre.Api.Auth;
using CaminhoLivre.Api.Contracts;
using CaminhoLivre.Api.Data;
using CaminhoLivre.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CaminhoLivre.Api.Services;

public class ReporteService(AppDbContext db, TimeProvider relogio)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public Task<Categoria?> BuscarCategoriaAsync(string codigo) =>
        db.Categorias.Include(c => c.Orgao)
            .FirstOrDefaultAsync(c => c.Codigo == codigo.Trim().ToUpper());

    public Task<Usuario?> BuscarUsuarioAsync(long usuarioId) =>
        db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId);

    // O cadastro é feito na API Java. Se o token é válido (assinado com a JWT_SECRET) mas o usuário
    // ainda não está na base desta API, ele é registrado na hora com os dados do próprio token.
    public async Task<Usuario?> ObterOuRegistrarUsuarioAsync(ClaimsPrincipal principal)
    {
        var id = principal.ObterUsuarioId();
        var existente = await BuscarUsuarioAsync(id);
        if (existente is not null)
        {
            return existente;
        }

        var nome = principal.FindFirstValue("name");
        var usuario = new Usuario
        {
            Id = id,
            Nome = string.IsNullOrWhiteSpace(nome) ? $"Usuário {id}" : nome.Trim()[..Math.Min(nome.Trim().Length, 120)],
            Email = $"usuario{id}@voz-e-via.local",
            Role = principal.FindFirstValue("role") ?? Papeis.Cidadao,
            CriadoEm = Agora
        };

        db.Usuarios.Add(usuario);
        try
        {
            await db.SaveChangesAsync();
            db.Entry(usuario).State = EntityState.Detached;
            return usuario;
        }
        catch (DbUpdateException)
        {
            // Duas requisições do mesmo usuário novo ao mesmo tempo: a outra já registrou.
            db.ChangeTracker.Clear();
            return await BuscarUsuarioAsync(id);
        }
    }

    public async Task<Reporte> CriarAsync(long usuarioId, Categoria categoria, CriarReporteRequest request)
    {
        var agora = Agora;
        var urgencia = Enum.Parse<Urgencia>(request.Urgencia!, ignoreCase: true);

        var reporte = new Reporte
        {
            Protocolo = $"TMP-{Guid.NewGuid():N}"[..20],
            UsuarioId = usuarioId,
            CategoriaId = categoria.Id,
            OrgaoId = categoria.OrgaoId,
            Descricao = request.Descricao!.Trim(),
            Latitude = request.Latitude!.Value,
            Longitude = request.Longitude!.Value,
            Endereco = string.IsNullOrWhiteSpace(request.Endereco) ? null : request.Endereco.Trim(),
            FotoUrl = request.FotoUrl,
            Urgencia = urgencia,
            Status = StatusReporte.ABERTO,
            Prioridade = RegrasReporte.CalcularPrioridade(urgencia, agora, agora),
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        reporte.Historico.Add(NovoHistorico(null, StatusReporte.ABERTO, "Reporte registrado pelo cidadão.", usuarioId));

        await using var transacao = await db.Database.BeginTransactionAsync();

        db.Reportes.Add(reporte);
        await db.SaveChangesAsync();

        // O protocolo usa o ID gerado pelo banco, por isso é definido depois do primeiro insert.
        reporte.Protocolo = RegrasReporte.GerarProtocolo(reporte.Id, agora.Year);
        await db.SaveChangesAsync();

        await transacao.CommitAsync();

        reporte.Categoria = categoria;
        reporte.Orgao = categoria.Orgao;
        return reporte;
    }

    public async Task<PaginaResponse<ReporteResponse>> ListarDoUsuarioAsync(long usuarioId, int pagina, int tamanho)
    {
        var consulta = db.Reportes.AsNoTracking().Where(r => r.UsuarioId == usuarioId);

        var total = await consulta.CountAsync();
        var itens = await consulta
            .Include(r => r.Categoria)
            .Include(r => r.Orgao)
            .OrderByDescending(r => r.CriadoEm)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync();

        var totalPaginas = (int)Math.Ceiling(total / (double)tamanho);
        return new PaginaResponse<ReporteResponse>(itens.Select(ReporteMapper.ParaResponse), pagina, tamanho, total, totalPaginas);
    }

    public Task<Reporte?> BuscarCompletoAsync(long id) =>
        db.Reportes
            .Include(r => r.Categoria)
            .Include(r => r.Orgao)
            .Include(r => r.Historico)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task AtualizarAsync(Reporte reporte, AtualizarReporteRequest request)
    {
        reporte.Descricao = request.Descricao!.Trim();
        reporte.Endereco = string.IsNullOrWhiteSpace(request.Endereco) ? null : request.Endereco.Trim();
        reporte.FotoUrl = request.FotoUrl;
        reporte.AtualizadoEm = Agora;
        await db.SaveChangesAsync();
    }

    public async Task DefinirFotoAsync(Reporte reporte, string fotoUrl)
    {
        reporte.FotoUrl = fotoUrl;
        reporte.AtualizadoEm = Agora;
        await db.SaveChangesAsync();
    }

    public async Task CancelarAsync(Reporte reporte, long usuarioId)
    {
        reporte.Historico.Add(NovoHistorico(reporte.Status, StatusReporte.CANCELADO, "Cancelado pelo cidadão.", usuarioId));
        reporte.Status = StatusReporte.CANCELADO;
        reporte.AtualizadoEm = Agora;
        await db.SaveChangesAsync();
    }

    private HistoricoStatus NovoHistorico(StatusReporte? anterior, StatusReporte novo, string observacao, long usuarioId) => new()
    {
        StatusAnterior = anterior,
        StatusNovo = novo,
        Observacao = observacao,
        UsuarioId = usuarioId,
        CriadoEm = Agora
    };
}
