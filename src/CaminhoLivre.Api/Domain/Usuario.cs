namespace CaminhoLivre.Api.Domain;

// Cópia local de quem usa esta API. O cadastro e o login ficam na API Java (tabela usuarios no Azure SQL);
// aqui o usuário é registrado no primeiro uso, com os dados do token. OrgaoId só existe para o GESTOR.
public class Usuario
{
    // Mesmo ID da API Java (claim "sub"), nunca gerado aqui.
    public long Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = Papeis.Cidadao;
    public long? OrgaoId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Orgao? Orgao { get; set; }
}
