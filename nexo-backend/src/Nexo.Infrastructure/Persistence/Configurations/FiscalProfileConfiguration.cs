using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations;

/// <summary>fiscal_profiles — the nota issuer data of a store; one row per store.</summary>
internal sealed class FiscalProfileConfiguration : IEntityTypeConfiguration<FiscalProfile>
{
    public void Configure(EntityTypeBuilder<FiscalProfile> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("fiscal_profiles");
        b.HasIndex(x => new { x.TenantId, x.StoreId }).IsUnique().HasDatabaseName("ux_fiscal_profiles_tenant_store");

        b.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.TradeName).HasColumnName("trade_name").HasMaxLength(200);
        b.Property(x => x.Cnpj).HasColumnName("cnpj").HasMaxLength(14).IsRequired();
        b.Property(x => x.StateRegistration).HasColumnName("state_registration").HasMaxLength(14).IsRequired();
        b.Property(x => x.MunicipalRegistration).HasColumnName("municipal_registration").HasMaxLength(20);
        b.Property(x => x.TaxRegime).HasColumnName("tax_regime").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.Street).HasColumnName("street").HasMaxLength(200).IsRequired();
        b.Property(x => x.Number).HasColumnName("number").HasMaxLength(20).IsRequired();
        b.Property(x => x.Complement).HasColumnName("complement").HasMaxLength(100);
        b.Property(x => x.District).HasColumnName("district").HasMaxLength(100).IsRequired();
        b.Property(x => x.CityName).HasColumnName("city_name").HasMaxLength(100).IsRequired();
        b.Property(x => x.CityIbgeCode).HasColumnName("city_ibge_code").HasMaxLength(7).IsRequired();
        b.Property(x => x.State).HasColumnName("state").HasMaxLength(2).IsRequired();
        b.Property(x => x.ZipCode).HasColumnName("zip_code").HasMaxLength(8).IsRequired();
        b.Ignore(x => x.Crt);
    }
}
