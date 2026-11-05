using System.Collections.Concurrent;
using Nexo.Application.Integrations.Billing;
using Stripe;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// O "Stripe" dos testes do webhook: guarda o estado atual de cada assinatura e responde às leituras sem sair do host de
/// teste. Uma assinatura sem estado definido lança erro, para que nenhum caminho leia o Stripe sem o teste saber.
/// </summary>
public sealed class FakeStripeSubscriptionFetcher : IStripeSubscriptionFetcher
{
    private readonly ConcurrentDictionary<string, StripeSubscriptionSnapshot> _current = new();
    private readonly ConcurrentDictionary<string, bool> _failing = new();
    private readonly ConcurrentDictionary<string, int> _reads = new();
    private readonly ConcurrentDictionary<string, ReadGate> _gates = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _peerWaits = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _peerArrived = new();

    /// <summary>Define o estado atual da assinatura no "Stripe" (o que a próxima leitura devolve).</summary>
    public void Set(string subscriptionId, string status, string priceId = "price_monthly", string interval = "month",
        DateTime? periodStart = null, DateTime? periodEnd = null, bool cancelAtPeriodEnd = false)
    {
        _failing.TryRemove(subscriptionId, out _);
        _current[subscriptionId] = new StripeSubscriptionSnapshot(subscriptionId, status, priceId, interval,
            periodStart ?? DateTime.UtcNow.AddDays(-5), periodEnd ?? DateTime.UtcNow.AddDays(25), cancelAtPeriodEnd);
    }

    /// <summary>As próximas leituras falham como falharia a chamada real.</summary>
    public void Fail(string subscriptionId) => _failing[subscriptionId] = true;

    public int Reads(string subscriptionId) => _reads.GetValueOrDefault(subscriptionId);

    /// <summary>
    /// A próxima leitura captura o estado e fica "em voo" até <see cref="ReadGate.Release"/>: é o intervalo em que o Stripe
    /// já respondeu, mas a resposta ainda não foi gravada no banco.
    /// </summary>
    public ReadGate HoldNextRead(string subscriptionId) => _gates[subscriptionId] = new ReadGate();

    /// <summary>
    /// Cada leitura espera outra leitura da mesma assinatura por até <paramref name="wait"/>: garante que entregas
    /// simultâneas se sobreponham de fato. Se a outra entrega estiver bloqueada antes da leitura, o tempo só se esgota.
    /// </summary>
    public void WaitForOverlap(string subscriptionId, TimeSpan wait) => _peerWaits[subscriptionId] = wait;

    public async Task<StripeSubscriptionSnapshot> GetAsync(string subscriptionId, CancellationToken ct = default)
    {
        var read = _reads.AddOrUpdate(subscriptionId, 1, (_, n) => n + 1);

        if (_failing.ContainsKey(subscriptionId))
            throw new StripeException("Falha simulada ao ler a assinatura.");
        if (!_current.TryGetValue(subscriptionId, out var snapshot))
            throw new InvalidOperationException($"Leitura inesperada de {subscriptionId}: o teste não definiu esse estado.");

        if (_peerWaits.TryGetValue(subscriptionId, out var wait))
        {
            var arrived = _peerArrived.GetOrAdd(subscriptionId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            if (read > 1) arrived.TrySetResult();
            await Task.WhenAny(arrived.Task, Task.Delay(wait, ct));
        }

        if (_gates.TryRemove(subscriptionId, out var gate))
            await gate.WaitAsync(ct);

        return snapshot;
    }

    public sealed class ReadGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completa quando a leitura já capturou o estado.</summary>
        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        /// <summary>A leitura em voo termina em erro, como uma falha de rede depois de já ter começado.</summary>
        public void ReleaseWithFailure() => _released.TrySetException(new StripeException("Falha simulada ao ler a assinatura."));

        internal async Task WaitAsync(CancellationToken ct)
        {
            _entered.TrySetResult();
            await _released.Task.WaitAsync(ct);
        }
    }
}
