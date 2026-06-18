using System;
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

/// <summary>
/// Domain coverage for SvcSettings — the per-store internal preset, validated against the
/// registry (the vertical "ramo" is config, not a module).
/// </summary>
public class SvcSettingsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store  = Guid.NewGuid();

    [Fact]
    public void Create_with_a_valid_preset_key_normalizes_and_sets_it()
    {
        var s = SvcSettings.Create(Tenant, "Salao-Beleza");
        s.PresetKey.Should().Be("salao-beleza");
        s.TenantId.Should().Be(Tenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-vertical")]
    [InlineData("varejo")]
    [InlineData("service")] // the module key is NOT a valid preset key
    public void Create_with_an_invalid_preset_key_throws(string key)
    {
        Action act = () => SvcSettings.Create(Tenant, key);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreateForStore_sets_both_tenant_and_store()
    {
        var s = SvcSettings.CreateForStore(Tenant, Store, "pet-shop");
        s.TenantId.Should().Be(Tenant);
        s.StoreId.Should().Be(Store);
        s.PresetKey.Should().Be("pet-shop");
    }

    [Fact]
    public void SetPreset_changes_to_another_valid_vertical()
    {
        var s = SvcSettings.Create(Tenant, "salao-beleza");
        s.SetPreset("oficina-mecanica");
        s.PresetKey.Should().Be("oficina-mecanica");
    }

    [Fact]
    public void SetPreset_rejects_an_invalid_key_and_keeps_the_previous_one()
    {
        var s = SvcSettings.Create(Tenant, "salao-beleza");
        Action act = () => s.SetPreset("nope");
        act.Should().Throw<DomainException>();
        s.PresetKey.Should().Be("salao-beleza");
    }
}
