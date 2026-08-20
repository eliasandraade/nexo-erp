using System;
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
/// Effective-preset resolution: the stored per-store SvcSettings row is the only source. There
/// is no fallback to a module key — the retired per-vertical SKUs were rewritten to "service"
/// (and their vertical copied into SvcSettings) by ConvertLegacyServiceSubscriptions. Never
/// auto-picks a preset.
/// </summary>
public class SvcSettingsServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static SvcSettingsService Build(SvcSettings? stored, bool resolved = true)
    {
        var repo = Substitute.For<ISvcSettingsRepository>();
        repo.GetForCurrentStoreAsync(Arg.Any<CancellationToken>()).Returns(stored);

        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsResolved.Returns(resolved);
        tenant.Id.Returns(Tenant);

        return new SvcSettingsService(repo, tenant);
    }

    [Fact]
    public async Task Not_configured_when_the_store_has_no_settings_row()
    {
        var dto = await Build(stored: null).GetSettingsAsync();
        dto.IsConfigured.Should().BeFalse();
        dto.PresetKey.Should().BeNull();
    }

    [Fact]
    public async Task Uses_the_stored_preset_when_present()
    {
        var dto = await Build(SvcSettings.Create(Tenant, "pet-shop")).GetSettingsAsync();
        dto.IsConfigured.Should().BeTrue();
        dto.PresetKey.Should().Be("pet-shop");
    }

    [Fact]
    public async Task Uses_the_stored_preset_for_a_barbershop()
    {
        var dto = await Build(SvcSettings.Create(Tenant, "barbearia")).GetSettingsAsync();
        dto.IsConfigured.Should().BeTrue();
        dto.PresetKey.Should().Be("barbearia");
    }

    [Fact]
    public async Task Resolve_is_null_when_tenant_is_not_resolved()
    {
        var key = await Build(stored: null, resolved: false).ResolveEffectivePresetKeyAsync();
        key.Should().BeNull();
    }
}
