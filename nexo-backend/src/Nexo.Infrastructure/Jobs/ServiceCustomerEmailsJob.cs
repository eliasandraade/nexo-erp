using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Jobs;

/// <summary>
/// E-mails Orken Service customers, for stores that turned it on (SvcSettings.CustomerEmailsEnabled):
/// the confirmation of a portal booking, and a reminder <c>ReminderHoursBefore</c> hours before the
/// appointment — both with the customer's own link to view or cancel it.
///
/// A system job across every tenant, every two minutes. Each e-mail is claimed with a conditional
/// UPDATE before it is sent, so several instances never send it twice: at most once, never retried
/// (a provider failure is logged, not repeated to the customer).
/// </summary>
public class ServiceCustomerEmailsJob : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int MaxReminderHours = 72;
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ServiceCustomerEmailsJob> _logger;

    public ServiceCustomerEmailsJob(IServiceScopeFactory scopes, ILogger<ServiceCustomerEmailsJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRun, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var frontendUrl = scope.ServiceProvider.GetRequiredService<IConfiguration>()["App:FrontendUrl"] ?? "http://localhost:5173";
                var sent = await RunOnceAsync(db, email, frontendUrl, DateTime.UtcNow, _logger, stoppingToken);
                if (sent > 0) _logger.LogInformation("Service customer e-mails sent: {Count}", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Service customer e-mails job failed; retrying in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal sealed record Candidate(
        Guid Id, DateTime StartsAt, string Token, string? Email, string CustomerName, string ServiceName,
        string ProfessionalName, string StoreName, string? Slug, string TimeZoneId, int ReminderHours, bool ConfirmationPending,
        DateTime? ReminderSentAt, SvcAppointmentStatus Status);

    /// <summary>One pass: pending confirmations, then due reminders. Returns how many e-mails went out.</summary>
    public static async Task<int> RunOnceAsync(
        NexoDbContext db, IEmailService email, string frontendUrl, DateTime nowUtc, ILogger logger, CancellationToken ct)
    {
        var horizon = nowUtc.AddHours(MaxReminderHours);
        var candidates = await (
                from a in db.SvcAppointments.IgnoreQueryFilters()
                join s in db.SvcSettings.IgnoreQueryFilters() on new { a.TenantId, a.StoreId } equals new { s.TenantId, s.StoreId }
                join c in db.Customers.IgnoreQueryFilters() on a.CustomerId equals c.Id
                join st in db.Stores.IgnoreQueryFilters() on a.StoreId equals st.Id
                join cat in db.SvcCatalogItems.IgnoreQueryFilters() on a.CatalogItemId equals cat.Id
                join p in db.SvcProfessionals.IgnoreQueryFilters() on a.ProfessionalId equals p.Id
                where s.CustomerEmailsEnabled && a.PublicToken != null && c.Email != null
                   && a.StartsAt > nowUtc
                   && (a.Status == SvcAppointmentStatus.Scheduled || a.Status == SvcAppointmentStatus.Confirmed)
                   && (a.ConfirmationEmailPending || (a.ReminderSentAt == null && a.StartsAt <= horizon))
                orderby a.StartsAt
                select new Candidate(
                    a.Id, a.StartsAt, a.PublicToken!, c.Email, c.Name, cat.Name, p.Name,
                    s.DisplayName ?? st.Name, st.PublicSlug, s.TimeZoneId, s.ReminderHoursBefore,
                    a.ConfirmationEmailPending, a.ReminderSentAt, a.Status))
            .AsNoTracking()
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var c in candidates)
        {
            if (c.ConfirmationPending && await ClaimConfirmationAsync(db, c.Id, ct)
                && await SendAsync(email, c, isReminder: false, frontendUrl, logger, ct))
                sent++;

            var reminderDue = c.ReminderSentAt is null && c.StartsAt <= nowUtc.AddHours(c.ReminderHours);
            // a booking made inside the reminder window just got its confirmation: no reminder on top of it
            if (reminderDue && c.ConfirmationPending) { await ClaimReminderAsync(db, c.Id, nowUtc, ct); continue; }
            if (reminderDue && await ClaimReminderAsync(db, c.Id, nowUtc, ct)
                && await SendAsync(email, c, isReminder: true, frontendUrl, logger, ct))
                sent++;
        }
        return sent;
    }

    private static async Task<bool> ClaimConfirmationAsync(NexoDbContext db, Guid id, CancellationToken ct)
        => await db.SvcAppointments.IgnoreQueryFilters()
            .Where(a => a.Id == id && a.ConfirmationEmailPending)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ConfirmationEmailPending, false), ct) == 1;

    private static async Task<bool> ClaimReminderAsync(NexoDbContext db, Guid id, DateTime nowUtc, CancellationToken ct)
        => await db.SvcAppointments.IgnoreQueryFilters()
            .Where(a => a.Id == id && a.ReminderSentAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReminderSentAt, nowUtc), ct) == 1;

    private static async Task<bool> SendAsync(
        IEmailService email, Candidate c, bool isReminder, string frontendUrl, ILogger logger, CancellationToken ct)
    {
        try
        {
            var (subject, html) = BuildEmail(c, isReminder, frontendUrl);
            return await email.SendAsync(c.Email!, subject, html, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Service e-mail for appointment {ApptId} failed", c.Id);
            return false;
        }
    }

    internal static (string Subject, string Html) BuildEmail(Candidate c, bool isReminder, string frontendUrl)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(c.StartsAt, DateTimeKind.Utc), ResolveTimeZone(c.TimeZoneId));
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var when = local.ToString("dddd, dd/MM 'às' HH:mm", culture);
        var link = c.Slug is null ? null : $"{frontendUrl.TrimEnd('/')}/agendar/{c.Slug}/reserva/{c.Token}";
        var e = (string v) => WebUtility.HtmlEncode(v);

        var subject = isReminder
            ? $"Lembrete: {c.ServiceName} {local.ToString("dd/MM 'às' HH:mm", culture)} — {c.StoreName}"
            : $"Agendamento recebido — {c.StoreName}";
        var intro = isReminder
            ? "Passando para lembrar do seu horário:"
            : c.Status == SvcAppointmentStatus.Confirmed ? "Seu horário está confirmado:" : "Recebemos seu agendamento:";
        var linkBlock = link is null ? "" : $"""
            <p style="margin:24px 0 0"><a href="{e(link)}" style="color:#4f46e5;font-weight:600">Ver ou cancelar este agendamento</a></p>
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f6f7f9;font-family:Arial,Helvetica,sans-serif;color:#111827">
              <div style="max-width:480px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px">
                <p style="margin:0 0 4px;font-size:13px;color:#6b7280">{e(c.StoreName)}</p>
                <p style="margin:0 0 16px;font-size:15px">Olá, {e(c.CustomerName)}. {intro}</p>
                <p style="margin:0;font-size:18px;font-weight:700">{e(c.ServiceName)}</p>
                <p style="margin:6px 0 0;font-size:15px">{e(when)}</p>
                <p style="margin:4px 0 0;font-size:14px;color:#6b7280">com {e(c.ProfessionalName)}</p>
                {linkBlock}
              </div>
              <p style="max-width:480px;margin:12px auto 0;font-size:11px;color:#9ca3af;text-align:center">Enviado pelo Orken a pedido de {e(c.StoreName)}.</p>
            </body></html>
            """;
        return (subject, html);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        foreach (var candidate in new[] { id, "America/Sao_Paulo" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
