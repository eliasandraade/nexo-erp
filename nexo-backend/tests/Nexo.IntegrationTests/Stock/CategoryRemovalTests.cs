using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Categories;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Stock;

/// <summary>
/// "Remover categoria" in the products screen deactivates it (there is no hard delete):
/// the category leaves the default list, stays reachable with includeInactive (products keep it),
/// and a parent whose subcategories were all removed can be removed too.
/// </summary>
[Collection("Integration")]
public class CategoryRemovalTests
{
    private readonly HttpClient _client;

    public CategoryRemovalTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<CategoryDto> CreateAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, ParentCategoryId: parentId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    [Fact]
    public async Task Removed_category_leaves_the_list_but_is_still_readable_with_inactive()
    {
        await AuthenticateAsync();
        var category = await CreateAsync($"Removida {Guid.NewGuid():N}");

        (await _client.PostAsync($"/api/categories/{category.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var active = await _client.GetFromJsonAsync<List<CategoryDto>>("/api/categories");
        active!.Should().NotContain(c => c.Id == category.Id);

        var all = await _client.GetFromJsonAsync<List<CategoryDto>>("/api/categories?includeInactive=true");
        all!.Should().ContainSingle(c => c.Id == category.Id).Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Parent_is_blocked_by_active_children_only()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync($"Pai {Guid.NewGuid():N}");
        var child = await CreateAsync($"Filha {Guid.NewGuid():N}", parent.Id);

        (await _client.PostAsync($"/api/categories/{parent.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _client.PostAsync($"/api/categories/{child.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.PostAsync($"/api/categories/{parent.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
