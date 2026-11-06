using GridPulse.Core.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GridPulse.Ingestion.Alerts;

/// <summary>
/// Emails alerts that start firing. Throttled twice: a cooldown per (rule, meter), and a global
/// per-minute cap, so an outage of many meters becomes a few emails and one log line, not hundreds of emails.
/// </summary>
public sealed class EmailAlertNotifier(IOptions<EmailOptions> options, TimeProvider time, ILogger<EmailAlertNotifier> logger) : IAlertNotifier
{
    private readonly EmailOptions _options = options.Value;
    private readonly Dictionary<(int RuleId, int MeterId), DateTimeOffset> _lastSent = [];
    private readonly Queue<DateTimeOffset> _recent = new();
    private int _suppressed;

    public async Task NotifyAsync(AlertNotification alert, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host) || alert.State != AlertState.Firing)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (!ShouldSend((alert.RuleId, alert.MeterId), now))
        {
            _suppressed++;
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(_options.To));
        message.Subject = $"[GridPulse] {alert.RuleName}: {alert.MeterSerial}";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                {alert.Message}

                Meter:  {alert.MeterSerial} (id {alert.MeterId})
                Rule:   {alert.RuleName}
                Raised: {alert.RaisedAt:yyyy-MM-dd HH:mm:ss} UTC
                {(_suppressed > 0 ? $"\n{_suppressed} other alert emails were suppressed by throttling since the last email." : string.Empty)}
                """,
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        _suppressed = 0;
        logger.LogInformation("Emailed alert {AlertId} for {Meter}", alert.AlertId, alert.MeterSerial);
    }

    private bool ShouldSend((int, int) key, DateTimeOffset now)
    {
        if (_lastSent.TryGetValue(key, out var last) && now - last < _options.Cooldown)
        {
            return false;
        }

        while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromMinutes(1))
        {
            _recent.Dequeue();
        }

        if (_recent.Count >= _options.MaxPerMinute)
        {
            return false;
        }

        _recent.Enqueue(now);
        _lastSent[key] = now;
        return true;
    }
}
