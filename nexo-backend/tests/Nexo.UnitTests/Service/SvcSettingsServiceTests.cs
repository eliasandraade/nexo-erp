using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>
/// Effective-preset resolution: stored SvcSettings first, then the temporary legacy fallback to
/// a per-vertical module key, else not configured. Never auto-picks a preset.
/// </summary>
public class SvcSettingsServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static SvcSettingsService Build(
        SvcSettings? stored, IReadOnlyList<string> activeModules, bool resolved = true)
    {
        var repo = Substitute.For<ISvcSettingsRepository>();
        repo.GetForCurrentStoreAsync(Arg.Any<CancellationToken>()).Returns(stored);

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsResolved.Returns(resolved);
        tenant.Id.Returns(Tenant);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetActiveModuleKeysAsync(Tenant, Arg.Any<CancellationToken>()).Returns(activeModules);

        return new SvcSettingsService(repo, tenant, tenants);
    }

    [Fact]
    public async Task Not_configured_when_no_settings_and_no_legacy_vertical()
    {
        var dto = await Build(stored: null, activeModules: new[] { "service" }).GetSettingsAsync();
        dto.IsConfigured.Should().BeFalse();
        dto.PresetKey.Should().BeNull();
    }

    [Fact]
    public async Task Uses_the_stored_preset_when_present()
    {
        var dto = await Build(SvcSettings.Create(Tenant, "pet-shop"), new[] { "service" }).GetSettingsAsync();
        dto.IsConfigured.Should().BeTrue();
        dto.PresetKey.Should().Be("pet-shop");
    }

    [Fact]
    public async Task Falls_back_to_a_legacy_vertical_module_when_no_settings()
    {
        var dto = await Build(stored: null, activeModules: new[] { "salao-beleza" }).GetSettingsAsync();
        dto.IsConfigured.Should().BeTrue();
        dto.PresetKey.Should().Be("salao-beleza");
    }

    [Fact]
    public async Task Resolve_is_null_when_tenant_is_not_resolved()
    {
        var key = await Build(stored: null, activeModules: Array.Empty<string>(), resolved: false)
            .ResolveEffectivePresetKeyAsync();
        key.Should().BeNull();
    }
}
