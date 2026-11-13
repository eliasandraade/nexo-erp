using FluentAssertions;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;

namespace Nexo.UnitTests.Users;

/// <summary>A login has one spelling: what is stored is what every lookup and every comparison uses.</summary>
public class LoginNormalizationTests
{
    [Theory]
    [InlineData("maria", "maria")]
    [InlineData("MARIA", "maria")]
    [InlineData("  Maria.Silva ", "maria.silva")]
    [InlineData("\tadmin\r\n", "admin")]
    [InlineData("Pessoa@Empresa.COM.BR ", "pessoa@empresa.com.br")]
    [InlineData("joao_01-b", "joao_01-b")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void A_login_is_trimmed_and_lower_cased(string? typed, string stored)
        => User.NormalizeLogin(typed).Should().Be(stored);

    [Fact]
    public void Normalizing_twice_changes_nothing()
    {
        var once = User.NormalizeLogin("  Maria.SILVA ");

        User.NormalizeLogin(once).Should().Be(once);
    }

    [Fact]
    public void A_new_user_keeps_the_login_in_its_one_spelling()
    {
        var user = User.Create(Guid.NewGuid(), "Maria Silva", "maria@loja.com", "  Maria.SILVA ", "hash", UserRole.Vendedor);

        user.Login.Should().Be("maria.silva");
        user.Login.Should().Be(User.NormalizeLogin("MARIA.silva"));
    }
}
