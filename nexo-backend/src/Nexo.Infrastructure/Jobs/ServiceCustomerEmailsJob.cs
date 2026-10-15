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
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(600);   // provider rate limit
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

        var warnedNoProvider = false;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
                if (!email.CanDeliver)
                {
                    // nothing is claimed: once a provider is configured, the pending e-mails go out
                    if (!warnedNoProvider) _logger.LogWarning("No e-mail provider configured; Service customer e-mails are on hold.");
                    warnedNoProvider = true;
                    continue;
                }
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var frontendUrl = scope.ServiceProvider.GetRequiredService<IConfiguration>()["App:FrontendUrl"] ?? "http://localhost:5173";
                var sent = await RunOnceAsync(db, email, frontendUrl, DateTime.UtcNow, _logger, stoppingToken, Pause);
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

    internal sealed class Candidate
    {
        public Guid Id { get; init; }
        public DateTime StartsAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Token { get; init; } = "";
        public string Email { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string ServiceName { get; init; } = "";
        public string ProfessionalName { get; init; } = "";
        public string StoreName { get; init; } = "";
        public string? Slug { get; init; }
        public string TimeZoneId { get; init; } = "";
        public int ReminderHours { get; init; }
        public bool ConfirmationPending { get; init; }
        public DateTime? ReminderSentAt { get; init; }
        public SvcAppointmentStatus Status { get; init; }
    }

    /// <summary>Open, upcoming bookings of stores with e-mails on, whose customer has an address.</summary>
    private static IQueryable<Candidate> Candidates(NexoDbContext db, DateTime nowUtc) =>
        from a in db.SvcAppointments.IgnoreQueryFilters()
        join s in db.SvcSettings.IgnoreQueryFilters() on new { a.TenantId, a.StoreId } equals new { s.TenantId, s.StoreId }
        join c in db.Customers.IgnoreQueryFilters() on new { Id = a.CustomerId, a.TenantId } equals new { c.Id, c.TenantId }
        join st in db.Stores.IgnoreQueryFilters() on a.StoreId equals st.Id
        join cat in db.SvcCatalogItems.IgnoreQueryFilters() on a.CatalogItemId equals cat.Id
        join p in db.SvcProfessionals.IgnoreQueryFilters() on a.ProfessionalId equals p.Id
        where s.CustomerEmailsEnabled && a.PublicToken != null
           && c.IsActive && c.Email != null && c.Email != ""
           && a.StartsAt > nowUtc
           && (a.Status == SvcAppointmentStatus.Scheduled || a.Status == SvcAppointmentStatus.Confirmed)
        select new Candidate
        {
            Id = a.Id, StartsAt = a.StartsAt, CreatedAt = a.CreatedAt, Token = a.PublicToken!, Email = c.Email!,
            CustomerName = c.Name, ServiceName = cat.Name, ProfessionalName = p.Name,
            StoreName = s.DisplayName ?? st.Name, Slug = st.PublicSlug, TimeZoneId = s.TimeZoneId,
            ReminderHours = s.ReminderHoursBefore, ConfirmationPending = a.ConfirmationEmailPending,
            ReminderSentAt = a.ReminderSentAt, Status = a.Status,
        };

    /// <summary>
    /// One pass: pending confirmations, then due reminders — two queries, so rows not due yet never
    /// take a batch slot. Returns how many e-mails went out. <paramref name="pause"/> spaces the
    /// sends (provider rate limit).
    /// </summary>
    public static async Task<int> RunOnceAsync(
        NexoDbContext db, IEmailService email, string frontendUrl, DateTime nowUtc, ILogger logger, CancellationToken ct,
        TimeSpan? pause = null)
    {
        var confirmationsSince = nowUtc.AddDays(-1);      // a confirmation days late would only confuse
        var confirmations = await Candidates(db, nowUtc)
            .Where(x => x.ConfirmationPending && x.CreatedAt >= confirmationsSince)
            .OrderBy(x => x.CreatedAt).Take(BatchSize).AsNoTracking().ToListAsync(ct);
        var reminders = await Candidates(db, nowUtc)
            .Where(x => x.ReminderSentAt == null && x.StartsAt <= nowUtc.AddHours(x.ReminderHours))
            .OrderBy(x => x.StartsAt).Take(BatchSize).AsNoTracking().ToListAsync(ct);

        var sent = 0;
        var confirmed = new HashSet<Guid>();
        foreach (var c in confirmations)
        {
            if (!await ClaimConfirmationAsync(db, c.Id, nowUtc, ct)) continue;
            var result = await SendAsync(email, c, isReminder: false, frontendUrl, logger, ct);
            if (result == EmailSendResult.Sent) { sent++; confirmed.Add(c.Id); }
            else if (result == EmailSendResult.Failed) await ReleaseConfirmationAsync(db, c.Id, ct);
            await PauseAsync(pause, ct);
        }
        foreach (var c in reminders)
        {
            if (!await ClaimReminderAsync(db, c.Id, nowUtc, ct)) continue;
            // booked inside the reminder window and confirmed just now: no reminder on top of it
            if (confirmed.Contains(c.Id)) continue;
            var result = await SendAsync(email, c, isReminder: true, frontendUrl, logger, ct);
            if (result == EmailSendResult.Sent) sent++;
            else if (result == EmailSendResult.Failed) await ReleaseReminderAsync(db, c.Id, ct);
            await PauseAsync(pause, ct);
        }
        return sent;
    }

    private static Task PauseAsync(TimeSpan? pause, CancellationToken ct)
        => pause is { } p && p > TimeSpan.Zero ? Task.Delay(p, ct) : Task.CompletedTask;

    // Claims re-check the booking: one cancelled or moved since the read is not e-mailed.
    private static async Task<bool> ClaimConfirmationAsync(NexoDbContext db, Guid id, DateTime nowUtc, CancellationToken ct)
        => await db.SvcAppointments.IgnoreQueryFilters()
            .Where(a => a.Id == id && a.ConfirmationEmailPending && a.StartsAt > nowUtc
                     && (a.Status == SvcAppointmentStatus.Scheduled || a.Status == SvcAppointmentStatus.Confirmed))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ConfirmationEmailPending, false), ct) == 1;

    private static async Task<bool> ClaimReminderAsync(NexoDbContext db, Guid id, DateTime nowUtc, CancellationToken ct)
        => await db.SvcAppointments.IgnoreQueryFilters()
            .Where(a => a.Id == id && a.ReminderSentAt == null && a.StartsAt > nowUtc
                     && (a.Status == SvcAppointmentStatus.Scheduled || a.Status == SvcAppointmentStatus.Confirmed))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReminderSentAt, nowUtc), ct) == 1;

    // A temporary failure (rate limit, provider down, timeout) gives the e-mail back for the next pass.
    private static Task ReleaseConfirmationAsync(NexoDbContext db, Guid id, CancellationToken ct)
        => db.SvcAppointments.IgnoreQueryFilters().Where(a => a.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ConfirmationEmailPending, true), ct);

    private static Task ReleaseReminderAsync(NexoDbContext db, Guid id, CancellationToken ct)
        => db.SvcAppointments.IgnoreQueryFilters().Where(a => a.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReminderSentAt, (DateTime?)null), ct);

    private static async Task<EmailSendResult> SendAsync(
        IEmailService email, Candidate c, bool isReminder, string frontendUrl, ILogger logger, CancellationToken ct)
    {
        try
        {
            var (subject, html) = BuildEmail(c, isReminder, frontendUrl);
            return await email.SendAsync(c.Email, subject, html, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Service e-mail for appointment {ApptId} failed", c.Id);
            return EmailSendResult.Failed;
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
