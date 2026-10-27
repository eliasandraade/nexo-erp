using Nexo.Domain.Exceptions;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// Checks a rule version's content before it enters the catalogue, so the engine never meets a
/// rule it cannot run: known kinds only, sensible rates, known profile flags, non-empty notes.
/// </summary>
public static class TaxRuleContentValidator
{
    public static void Validate(string key, IReadOnlyList<TaxCondition> conditions, TaxRuleResult result)
    {
        if (key.StartsWith(TaxSimulator.HypothesisKeyPrefix, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Chaves HYPOTHESIS.* são reservadas às simulações.");

        foreach (var c in conditions)
        {
            switch (c)
            {
                case SupplierRegimeIn { Regimes.Count: 0 }:
                case SupplierRegimeNotIn { Regimes.Count: 0 }:
                    throw new DomainException("Condição de regime sem regimes.");
                case ItemKindIn { Kinds.Count: 0 }:
                    throw new DomainException("Condição de tipo de item sem tipos.");
                case ProfileFlagIs f when !TaxProfileFlags.All.Contains(f.Flag):
                    throw new DomainException($"Indicador de perfil desconhecido: {f.Flag}.");
                case null:
                    throw new DomainException("Condição vazia.");
            }
        }

        switch (result)
        {
            case RateResult r when r.RatePercent is < 0 or > 100 || decimal.Round(r.RatePercent, 4) != r.RatePercent:
                throw new DomainException("A alíquota vai de 0 a 100%, com até 4 casas decimais.");
            case NoteResult n when string.IsNullOrWhiteSpace(n.Code) || string.IsNullOrWhiteSpace(n.Text):
                throw new DomainException("Uma nota precisa de código e texto.");
            case NoteResult when conditions.Any(c => c is ItemKindIn or ItemGeneralRegime):
                throw new DomainException("Notas valem para a operação inteira: não use condição de tipo de item.");
            case null:
                throw new DomainException("A regra precisa de um resultado.");
        }
    }
}
