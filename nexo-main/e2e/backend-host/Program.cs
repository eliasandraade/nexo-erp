using System.Net.Http.Json;

using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

using Nexo.IntegrationTests.Helpers;

using Nexo.IntegrationTests.Common;

using Nexo.Infrastructure.Persistence;



// Development/test only. Real controllers and PostgreSQL, isolated from every existing database.

public static class PortalReviewHost {

 static readonly ReviewStorage Storage = new();
 public static async Task Main(string[] args) {

  var root = Environment.GetEnvironmentVariable("BUILD_BACKEND_ROOT") ?? throw new Exception("BUILD_BACKEND_ROOT required");

  Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_NEXO_API",Path.Combine(root,"src","Nexo.Api"));

  await using var factory = new ReviewFactory(root);

  await factory.InitializeAsync();

  using var admin = await AuthClientFactory.LoginAsAdminAsync(factory);

  using var client = factory.CreateApiClient();

  var company = "review-construtora";

  await Read(await admin.PutAsJsonAsync("/api/v1/build/portal/company",new {slug=company,displayName="Construtora · ambiente de teste"}));

  var password = "Review!" + Guid.NewGuid().ToString("N");

  var ids = new Dictionary<string,Guid>();

  foreach(var slug in new[]{"obra-publica","obra-privada","imovel-entregue"}) {

   var project = await Read(await admin.PostAsJsonAsync("/api/v1/build/projects",new {name=slug=="imovel-entregue"?"Meu imóvel · teste local":"Residência · teste local",clientName="Cliente de teste",type="House",startDate="2026-01-05",expectedEndDate="2026-12-20"}));

   var id=project.GetProperty("id").GetGuid();ids[slug]=id;

   var stage=await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/stages",new{name="Fundação",plannedStart="2026-01-05",plannedEnd="2026-03-31",weight=1m}));

   var stageId=stage.GetProperty("id").GetGuid();

   await Read(await admin.PutAsJsonAsync($"/api/v1/build/stages/{stageId}/progress",new {progressPercent=40m}));

   var update=await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/client-updates",new{title="Fundação em andamento",body="Registro publicado para a revisão local. Dados de teste.",kind="WeeklyDigest"}));

   await Read(await admin.PostAsync($"/api/v1/build/client-updates/{update.GetProperty("id").GetGuid()}/publish",null));

   if(slug!="obra-publica") {

    var co=await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/change-orders",new {title="Ajuste de revestimento",costImpact=200m,scheduleImpactDays=2,stageId}));

    await Read(await admin.PostAsync($"/api/v1/build/change-orders/{co.GetProperty("id").GetGuid()}/send",null));

    var sel=await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/selections",new {title="Piso da sala",clientVisible=true}));

    await Read(await admin.PostAsJsonAsync($"/api/v1/build/selections/{sel.GetProperty("id").GetGuid()}/options",new {name="Cerâmica padrão",priceDelta=0m,isStandard=true}));

   }

   if(slug=="imovel-entregue") {

    await Read(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}/handover",new {scheduledFor=DateTime.UtcNow.ToString("yyyy-MM-dd")}));

    await Read(await admin.PostAsync($"/api/v1/build/projects/{id}/handover/deliver",null));

   }

   await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/zones",new {kind="Area",name="Sala · medidas de teste",simplifiedBox=new{width=3,depth=4,height=2.8,elevation=0}}));

   var visibility=new {overallProgress=true,stageProgress=true,schedule=true,forecastDelivery=true,clientUpdates=true,dailyLogs=true,photos=true,financialSummary=false,postConstruction=true,changeOrders=true,selections=true,documents=true,model3d=true,realityCapture=true};

   await Read(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}/portal",new{slug,access="Public",title="Acompanhe sua obra",welcomeMessage="Registros de teste, servidos pelo backend local.",visibility}));

   if(slug!="obra-publica") {

    await Read(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}/portal/password",new {password}));

    await Read(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}/portal",new{slug,access="Password",title="Acompanhe sua obra",visibility}));

   }

   await Read(await admin.PostAsync($"/api/v1/build/projects/{id}/portal/publish",null));

  }

  var builder=WebApplication.CreateBuilder(args);builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:5194");

  var app=builder.Build();

  app.Use(async(ctx,next)=>{ctx.Response.Headers.AccessControlAllowOrigin="http://127.0.0.1:5195";ctx.Response.Headers.AccessControlAllowHeaders="Content-Type,X-Build-Portal-Session,If-None-Match";ctx.Response.Headers.AccessControlAllowMethods="GET,POST,DELETE,OPTIONS";ctx.Response.Headers.AccessControlExposeHeaders="Retry-After,ETag";if(ctx.Request.Method=="OPTIONS"){ctx.Response.StatusCode=204;return;}await next();});

  app.MapGet("/__test/media/{**key}",(string key)=>Storage.Inner.Objects.TryGetValue(key,out var value)?Results.File(value.Bytes,value.ContentType):Results.NotFound());
  app.MapPost("/__test/resolve",async()=>{
   var list=await Read(await admin.GetAsync($"/api/v1/build/projects/{ids["imovel-entregue"]}/assistance"));
   foreach(var item in list.EnumerateArray()) {var ticket=item.GetProperty("id").GetGuid();
    await Read(await admin.PostAsync($"/api/v1/build/assistance/{ticket}/triage",null));
    await Read(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{ticket}/schedule",new{scheduledFor=DateTimeOffset.UtcNow.AddDays(1)}));
    await Read(await admin.PostAsync($"/api/v1/build/assistance/{ticket}/start",null));
    await Read(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{ticket}/resolve",new{resolutionNotes="Ajuste concluído no ambiente de teste."}));
   }return Results.Ok();
  });
  app.MapGet("/__test/setup",()=>new{company,password,projects=ids.Keys});

  app.MapPost("/__test/expire",async()=>{using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<NexoDbContext>();foreach(var session in await db.BldPortalSessions.IgnoreQueryFilters().ToListAsync()) session.Revoke(DateTime.UtcNow);await db.SaveChangesAsync();return Results.Ok();});

  app.Map("/api/public/build/{**path}",async ctx=>{

   using var request=new HttpRequestMessage(new HttpMethod(ctx.Request.Method),ctx.Request.Path+ctx.Request.QueryString);

   if(ctx.Request.ContentLength>0 || ctx.Request.ContentType!=null){request.Content=new StreamContent(ctx.Request.Body);if(ctx.Request.ContentType!=null)request.Content.Headers.TryAddWithoutValidation("Content-Type",ctx.Request.ContentType);}

   foreach(var h in new[]{"X-Build-Portal-Session","If-None-Match"})if(ctx.Request.Headers.TryGetValue(h,out var value))request.Headers.TryAddWithoutValidation(h,value.ToString());

   using var response=await client.SendAsync(request);ctx.Response.StatusCode=(int)response.StatusCode;

   foreach(var h in response.Headers)ctx.Response.Headers[h.Key]=h.Value.ToArray();foreach(var h in response.Content.Headers)ctx.Response.Headers[h.Key]=h.Value.ToArray();ctx.Response.Headers.Remove("transfer-encoding");await response.Content.CopyToAsync(ctx.Response.Body);

  });

  Console.WriteLine("Portal integration host ready on 127.0.0.1:5194 (test data only)");await app.RunAsync();

 }

 static async Task<JsonElement> Read(HttpResponseMessage response){var content=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception($"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {content}");return string.IsNullOrWhiteSpace(content)?default:JsonDocument.Parse(content).RootElement.Clone();}

 sealed class ReviewStorage:IStorageProvider {
  public InMemoryStorageProvider Inner {get;}=new();long sequence;
  public Task<StorageUploadResult> UploadAsync(StorageUploadRequest r,CancellationToken ct=default)=>Inner.UploadAsync(r,ct);
  public Task DeleteAsync(string key,CancellationToken ct=default)=>Inner.DeleteAsync(key,ct);
  public Task PingAsync(CancellationToken ct=default)=>Task.CompletedTask;
  public Task<Stream> OpenReadAsync(string key,CancellationToken ct=default)=>Inner.OpenReadAsync(key,ct);
  public string GetSignedUrl(string key,TimeSpan ttl)=>$"https://storage.test/{key}?signature=review-{Interlocked.Increment(ref sequence)}&ttl={(int)ttl.TotalSeconds}";
 }
 sealed class ReviewFactory(string root):TestWebApplicationFactory {

  protected override void ConfigureWebHost(IWebHostBuilder builder){base.ConfigureWebHost(builder);builder.UseContentRoot(Path.Combine(root,"src","Nexo.Api"));builder.UseSetting("Logging:LogLevel:Default","Warning");builder.UseSetting("Integrations:Features:StorageEnabled","true");builder.ConfigureTestServices(services=>{services.RemoveAll<IStorageProvider>();services.AddSingleton<IStorageProvider>(Storage);});}

 }

}

