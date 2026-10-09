namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Who may operate Orken Service through the API. Mirrors the frontend, where every /service/*
/// screen is restricted to management (routes.ts → MGMT). Without it the restriction existed only
/// in the UI: a Vendedor/Estoquista/Cozinha token could void payments (posting to the financeiro),
/// cancel orders, change the preset or the public portal straight through the API.
/// </summary>
internal static class ServiceRoles
{
    public const string Management = "Gerente,Diretoria";
}
