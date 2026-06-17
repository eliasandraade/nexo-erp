namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A Service vertical descriptor. NOT a feature/permission table — it is a descriptor
/// (labels + capability flags) resolved from the tenant's active module key for the single
/// Service engine (decision D1/D2). Lower <see cref="Priority"/> wins when a tenant holds
/// more than one service-family key (Q5 deterministic fallback).
/// </summary>
public sealed record ServicePreset(
    string Key,
    string DisplayName,
    int Priority,
    ServiceLabels Labels,
    ServiceCapabilities Capabilities);
