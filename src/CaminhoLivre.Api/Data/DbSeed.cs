using CaminhoLivre.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CaminhoLivre.Api.Data;

// Dados iniciais usados quando a própria API cria as tabelas. No Oracle do grupo, órgãos e categorias vêm do
// script-bd.sql.
public static class DbSeed
{
    // Usuários de teste só existem no SQLite (desenvolvimento), com IDs reservados bem acima dos gerados pela
    // API Java. No banco compartilhado, os usuários vêm sempre dos tokens da API Java, com o ID dela.
    public const long PrimeiroIdDeTeste = 900_001;

    public static async Task PopularAsync(AppDbContext db, bool incluirUsuariosDeTeste)
    {
        if (await db.Orgaos.AnyAsync())
        {
            return;
        }

        var obras = new Orgao { Nome = "Secretaria de Obras", Area = AreaOrgao.OBRAS };
        var iluminacao = new Orgao { Nome = "Departamento de Iluminação Pública", Area = AreaOrgao.ILUMINACAO };
        var mobilidade = new Orgao { Nome = "Secretaria de Mobilidade", Area = AreaOrgao.MOBILIDADE };
        var limpeza = new Orgao { Nome = "Serviço de Limpeza Urbana", Area = AreaOrgao.LIMPEZA };
        db.Orgaos.AddRange(obras, iluminacao, mobilidade, limpeza);

        db.Categorias.AddRange(
            new Categoria { Codigo = "CALCADA", Nome = "Calçada danificada", Orgao = obras },
            new Categoria { Codigo = "RAMPA", Nome = "Rampa ausente ou obstruída", Orgao = obras },
            new Categoria { Codigo = "BURACO", Nome = "Buraco na via", Orgao = obras },
            new Categoria { Codigo = "ILUMINACAO", Nome = "Poste apagado", Orgao = iluminacao },
            new Categoria { Codigo = "SEMAFORO_SONORO", Nome = "Semáforo sonoro quebrado", Orgao = mobilidade },
            new Categoria { Codigo = "PISO_TATIL", Nome = "Piso tátil danificado", Orgao = mobilidade },
            new Categoria { Codigo = "LIXO", Nome = "Lixo ou entulho", Orgao = limpeza },
            new Categoria { Codigo = "OUTRO", Nome = "Outro problema", Orgao = obras });

        if (incluirUsuariosDeTeste)
        {
            var id = PrimeiroIdDeTeste;
            db.Usuarios.AddRange(
                new Usuario { Id = id++, Nome = "Maria Souza", Email = "maria@teste.com", Role = Papeis.Cidadao },
                new Usuario { Id = id++, Nome = "João Lima", Email = "joao@teste.com", Role = Papeis.Cidadao },
                new Usuario { Id = id++, Nome = "Carlos Gestor Obras", Email = "gestor.obras@teste.com", Role = Papeis.Gestor, Orgao = obras },
                new Usuario { Id = id++, Nome = "Ana Gestora Iluminação", Email = "gestor.iluminacao@teste.com", Role = Papeis.Gestor, Orgao = iluminacao },
                new Usuario { Id = id, Nome = "Paula Cuidadora", Email = "cuidadora@teste.com", Role = Papeis.Cuidador });
        }

        await db.SaveChangesAsync();
    }
}
