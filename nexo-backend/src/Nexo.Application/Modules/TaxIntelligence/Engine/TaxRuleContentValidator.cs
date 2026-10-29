using Nexo.Domain.Exceptions;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// Checks a rule version's content before it enters the catalogue, so the engine never meets a
/// rule it cannot run: known kinds only, no missing lists, every enum value defined, sensible
/// rates, known profile flags, non-empty notes. A rule that passed here and is activated can
/// never turn every calculation of its dates into an error.
/// </summary>
public static class TaxRuleContentValidator
{
    public static void Validate(string key, IReadOnlyList<TaxCondition>? conditions, TaxRuleResult? result)
    {
        if (key.StartsWith(TaxSimulator.HypothesisKeyPrefix, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Chaves HYPOTHESIS.* são reservadas às simulações.");
        if (conditions is null) throw new DomainException("Informe as condições (lista, pode ser vazia).");

        foreach (var c in conditions) ValidateCondition(c);
        ValidateResult(result, conditions);
    }

    private static void ValidateCondition(TaxCondition? c)
    {
        switch (c)
        {
            case SupplierRegimeIn x: Defined(x.Regimes, "regimes"); break;
            case SupplierRegimeNotIn x: Defined(x.Regimes, "regimes"); break;
            case ItemKindIn x: Defined(x.Kinds, "tipos de item"); break;
            case DirectionIs x: Defined(x.Direction, "direção"); break;
            case ProfileFlagIs x when x.Flag is null || !TaxProfileFlags.All.Contains(x.Flag):
                throw new DomainException($"Indicador de perfil desconhecido: {x.Flag}.");
            case ProfileFlagIs: break;
            case ItemGeneralRegime: break;
            case null: throw new DomainException("Condição vazia.");
            default: throw new DomainException($"Condição desconhecida: {c.GetType().Name}.");
        }
    }

    private static void ValidateResult(TaxRuleResult? result, IReadOnlyList<TaxCondition> conditions)
    {
        switch (result)
        {
            case BaseInclusion x: Defined(x.Component, "componente da base"); break;
            case BaseExclusion x: Defined(x.Component, "componente da base"); break;
            case RateResult r:
                Defined(r.Tax, "tributo");
                if (r.RatePercent is < 0 or > 100 || decimal.Round(r.RatePercent, 4) != r.RatePercent)
                    throw new DomainException("A alíquota vai de 0 a 100%, com até 4 casas decimais.");
                break;
            case NoteResult n:
                if (string.IsNullOrWhiteSpace(n.Code) || string.IsNullOrWhiteSpace(n.Text))
                    throw new DomainException("Uma nota precisa de código e texto.");
                if (conditions.Any(c => c is ItemKindIn or ItemGeneralRegime))
                    throw new DomainException("Notas valem para a operação inteira: não use condição de tipo de item.");
                break;
            case null: throw new DomainException("A regra precisa de um resultado.");
            default: throw new DomainException($"Resultado desconhecido: {result.GetType().Name}.");
        }
    }

    private static void Defined<T>(IReadOnlyList<T>? values, string what) where T : struct, Enum
    {
        if (values is null || values.Count == 0) throw new DomainException($"Condição sem {what}.");
        foreach (var v in values) Defined(v, what);
    }

    private static void Defined<T>(T value, string what) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"Valor inválido em {what}: {value}.");
    }
}
