namespace Sayra.Backend.Application.Abstractions.Security
{
    public interface IPasswordHasher
    {
        (string Hash, string Salt) HashPassword(string password);
        (string Hash, string Salt, string Algorithm, string Parameters) HashPasswordWithDetails(string password);
        bool VerifyPassword(string password, string hash, string salt);
        bool VerifyPassword(string password, string hash, string salt, string algorithm);
        bool NeedsRehash(string algorithm);
    }
}
