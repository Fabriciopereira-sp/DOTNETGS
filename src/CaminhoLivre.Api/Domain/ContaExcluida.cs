namespace CaminhoLivre.Api.Domain;

// Conta excluída pelo próprio usuário (LGPD). O token dela continua assinado e dentro da validade por algumas
// horas, então a autenticação consulta esta tabela e recusa o token na hora, como faz a API Java.
public class ContaExcluida
{
    public long UsuarioId { get; set; }
    public DateTime ExcluidaEm { get; set; } = DateTime.UtcNow;
}
