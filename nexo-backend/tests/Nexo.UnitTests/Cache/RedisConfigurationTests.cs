using System.Linq;
using System.Net;
using FluentAssertions;
using Nexo.Infrastructure.Cache;
using StackExchange.Redis;
using Xunit;

namespace Nexo.UnitTests.Cache;

/// <summary>
/// Unit coverage for <see cref="RedisConfiguration.BuildOptions"/> — the redis://|rediss://
/// URI handling that fixes Railway/Upstash connectivity (ConfigurationOptions.Parse cannot
/// read the URI scheme). These run without any live Redis.
/// </summary>
public class RedisConfigurationTests
{
    private static (string Host, int Port) FirstEndpoint(ConfigurationOptions o)
    {
        var ep = o.EndPoints.Single();
        return ep switch
        {
            DnsEndPoint dns => (dns.Host, dns.Port),
            IPEndPoint ip   => (ip.Address.ToString(), ip.Port),
            _               => (ep.ToString()!, 0),
        };
    }

    [Fact]
    public void NativeFormat_IsParsedUnchanged()
    {
        var o = RedisConfiguration.BuildOptions("localhost:6379");

        FirstEndpoint(o).Should().Be(("localhost", 6379));
        o.Ssl.Should().BeFalse();
        o.AbortOnConnectFail.Should().BeFalse("fail-open posture must be preserved");
    }

    [Fact]
    public void NativeFormat_WithPasswordAndSsl_IsParsed()
    {
        var o = RedisConfiguration.BuildOptions("myhost:6380,password=s3cret,ssl=true");

        FirstEndpoint(o).Should().Be(("myhost", 6380));
        o.Password.Should().Be("s3cret");
        o.Ssl.Should().BeTrue();
    }

    [Fact]
    public void RedisUri_WithDefaultUser_SetsHostPortPassword_NoSsl()
    {
        var o = RedisConfiguration.BuildOptions("redis://default:p%40ss@redis.railway.internal:6379");

        FirstEndpoint(o).Should().Be(("redis.railway.internal", 6379));
        o.Password.Should().Be("p@ss", "percent-encoded credentials must be decoded");
        o.User.Should().BeNull("the conventional 'default' user needs no explicit User");
        o.Ssl.Should().BeFalse();
        o.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void RedissUri_EnablesTls()
    {
        var o = RedisConfiguration.BuildOptions("rediss://default:secret@gusc1-x.upstash.io:6379");

        FirstEndpoint(o).Should().Be(("gusc1-x.upstash.io", 6379));
        o.Ssl.Should().BeTrue();
        o.Password.Should().Be("secret");
    }

    [Fact]
    public void RedisUri_PasswordOnly_SetsPassword()
    {
        var o = RedisConfiguration.BuildOptions("redis://:justpass@somehost:6390");

        FirstEndpoint(o).Should().Be(("somehost", 6390));
        o.Password.Should().Be("justpass");
        o.User.Should().BeNull();
    }

    [Fact]
    public void RedisUri_NonDefaultUser_SetsUser()
    {
        var o = RedisConfiguration.BuildOptions("redis://appuser:pw@host:6379");

        o.User.Should().Be("appuser");
        o.Password.Should().Be("pw");
    }

    [Fact]
    public void RedisUri_WithoutPort_DefaultsTo6379()
    {
        var o = RedisConfiguration.BuildOptions("redis://default:secret@hostonly");

        FirstEndpoint(o).Should().Be(("hostonly", 6379));
    }
}
