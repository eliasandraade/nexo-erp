using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Filters;
using Xunit;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// An <see cref="IFormFile"/> parameter makes the framework write the whole body to disk before the action runs, and before anything it
/// could refuse. So no action may have one, and a new upload route cannot reopen the problem by accident.
/// </summary>
public class UploadRouteGuardTests
{
    private static IEnumerable<MethodInfo> Actions()
        => typeof(Nexo.Api.Controllers.Integrations.StorageController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static bool TakesFormFile(ParameterInfo p)
        => typeof(IFormFile).IsAssignableFrom(p.ParameterType)
           || typeof(IFormFileCollection).IsAssignableFrom(p.ParameterType)
           || typeof(IEnumerable<IFormFile>).IsAssignableFrom(p.ParameterType)
           || typeof(IFormCollection).IsAssignableFrom(p.ParameterType);

    [Fact]
    public void No_action_binds_a_form_file()
    {
        var offenders = Actions()
            .Where(a => a.GetParameters().Any(TakesFormFile))
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}");

        offenders.Should().BeEmpty("binding a file writes the body to disk before the action can refuse it");
    }

    [Fact]
    public void No_action_binds_a_form_field()
    {
        var offenders = Actions()
            .Where(a => a.GetParameters().Any(p => p.GetCustomAttribute<FromFormAttribute>() is not null))
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}");

        offenders.Should().BeEmpty("binding one form field reads the whole form, files included, before the action runs");
    }

    [Fact]
    public void Every_upload_action_keeps_the_form_away_from_the_binder()
    {
        var offenders = Actions()
            .Where(a => a.GetCustomAttribute<MultipartFileBodyAttribute>() is not null
                        && a.GetCustomAttribute<DisableFormValueModelBindingAttribute>() is null)
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}");

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Every_upload_action_has_a_rate_limit()
    {
        var offenders = Actions()
            .Where(a => a.GetCustomAttribute<MultipartFileBodyAttribute>() is not null
                        && a.GetCustomAttribute<EnableRateLimitingAttribute>() is null)
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}");

        offenders.Should().BeEmpty();
    }
}
