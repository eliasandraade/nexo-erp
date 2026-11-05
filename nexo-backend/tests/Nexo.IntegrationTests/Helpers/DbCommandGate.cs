using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Segura um comando SQL em andamento, por ordem do teste, até o teste mandar seguir. É o que torna determinística uma
/// corrida entre a requisição que lê do banco e a que muda a mesma linha: quem decide a ordem é o teste, nunca um sleep.
/// Sem nada armado, o interceptor não faz nada.
/// </summary>
public sealed class DbCommandGate : DbCommandInterceptor
{
    public enum Moment
    {
        /// <summary>Antes de o comando rodar: o que ele lê é o que estiver no banco quando for solto.</summary>
        BeforeRunning,
        /// <summary>Depois de o comando rodar: o resultado já está fixado, e quem o leu fica parado com ele na mão.</summary>
        AfterRunning,
    }

    private Hold? _armed;
    private Func<DbCommand, bool>? _counted;
    private int _count;

    /// <summary>Segura o próximo comando que atender a <paramref name="match"/>, uma vez só.</summary>
    public Hold HoldNext(Func<DbCommand, bool> match, Moment moment)
    {
        var hold = new Hold(match, moment);
        Volatile.Write(ref _armed, hold);
        return hold;
    }

    /// <summary>Passa a contar os comandos que atendem a <paramref name="match"/>; devolve como ler a contagem.</summary>
    public Func<int> Count(Func<DbCommand, bool> match)
    {
        Interlocked.Exchange(ref _count, 0);
        Volatile.Write(ref _counted, match);
        return () => Volatile.Read(ref _count);
    }

    /// <summary>A leitura do carimbo que a validação de sessão faz a cada requisição (só a coluna do carimbo).</summary>
    public static Func<DbCommand, bool> StampReadOf(Guid userId)
        => command => Regex.IsMatch(command.CommandText, """^\s*SELECT\s+\w+\."SecurityStamp"\s+FROM\b""", RegexOptions.None, TimeSpan.FromSeconds(1))
                      && HasParameter(command, userId);

    /// <summary>A leitura da linha inteira do usuário (renovação de sessão): papel e carimbo vêm juntos, do mesmo instante.</summary>
    public static Func<DbCommand, bool> UserRowOf(Guid userId)
        => command => IsUserRow(command) && HasParameter(command, userId);

    /// <summary>A leitura da linha do usuário pelo login (entrada no sistema).</summary>
    public static Func<DbCommand, bool> UserRowOfLogin(string login)
        => command => IsUserRow(command) && HasParameter(command, login);

    private static bool IsUserRow(DbCommand command)
        => command.CommandText.Contains("\"SecurityStamp\"", StringComparison.Ordinal)
           && command.CommandText.Contains(".role", StringComparison.Ordinal);

    private static bool HasParameter(DbCommand command, object value)
        => command.Parameters.Cast<DbParameter>().Any(p => value.Equals(p.Value));

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _counted) is { } counted && counted(command)) Interlocked.Increment(ref _count);
        await WaitIfHeldAsync(command, Moment.BeforeRunning);
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        await WaitIfHeldAsync(command, Moment.AfterRunning);
        return result;
    }

    private Task WaitIfHeldAsync(DbCommand command, Moment moment)
    {
        var hold = Volatile.Read(ref _armed);
        if (hold is null || hold.Moment != moment || !hold.Matches(command)) return Task.CompletedTask;
        if (Interlocked.CompareExchange(ref _armed, null, hold) != hold) return Task.CompletedTask;
        return hold.ReachedAndWaitAsync();
    }

    /// <summary>Um comando segurado. Sair do <c>using</c> sempre solta, para um teste que falhou não deixar a requisição presa.</summary>
    public sealed class Hold : IDisposable
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
        private readonly Func<DbCommand, bool> _match;
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Hold(Func<DbCommand, bool> match, Moment moment)
        {
            _match = match;
            Moment = moment;
        }

        internal Moment Moment { get; }

        internal bool Matches(DbCommand command) => _match(command);

        internal async Task ReachedAndWaitAsync()
        {
            _reached.TrySetResult();
            await _released.Task;
        }

        /// <summary>Espera o comando chegar ao ponto combinado.</summary>
        public Task ReachedAsync() => _reached.Task.WaitAsync(Limit);

        public void Release() => _released.TrySetResult();

        public void Dispose() => Release();
    }
}
