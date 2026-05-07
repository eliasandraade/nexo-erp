namespace Nexo.Domain.Modules.Interpreter;

// Deterministic, culture-invariant, idempotent:
// same input → same output regardless of machine locale or call order.
// Pipeline: trim → lowercase → remove accents → tokenize
//           → remove global stopwords → remove tenant stopwords → normalize whitespace.
public interface IDescriptionNormalizer
{
    string Normalize(string input, Guid tenantId);
}
