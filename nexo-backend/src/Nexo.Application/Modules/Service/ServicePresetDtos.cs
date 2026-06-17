using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Wire shape of the resolved Service preset. Composes the domain value descriptors
/// (labels + capabilities) directly so the response contract has a single definition —
/// serialized camelCase, enums as string names (global JsonStringEnumConverter).
/// <see cref="ServicePreset.Priority"/> is intentionally not exposed.
/// </summary>
public sealed record ServicePresetDto(
    string Key,
    string DisplayName,
    ServiceLabels Labels,
    ServiceCapabilities Capabilities);
