namespace Nexo.Domain.Exceptions;

/// <summary>
/// The change would leave the company without any active Diretoria user, and nobody else could administer it.
/// Answered as 409 Conflict with the code <see cref="ErrorCode"/>.
/// </summary>
public sealed class LastDirectorException : ConflictException
{
    public const string ErrorCode = "last_director";

    public LastDirectorException()
        : base("A empresa precisa ter pelo menos um usuário ativo na Diretoria. Promova ou reative outra pessoa na Diretoria antes de fazer esta alteração.", ErrorCode)
    {
    }
}
