using System.Net;
using System.Text;
using Coldstart.Application.Harvest;
using Coldstart.Application.Notifications;
using Coldstart.Domain;
using Coldstart.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class BriefingChannelTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Request = r;
            Body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok", Encoding.UTF8, "text/plain") };
        }
    }

    private static readonly IOptions<NotificationsOptions> Base =
        Options.Create(new NotificationsOptions { BaseUrl = "https://getcoldstart.nl/" });

    private static BriefingNotification Note(Customer c) => new(c.Id.Value, c.Name,
        [new SavedLead(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Acme", "Congrats + an idea"),
         new SavedLead(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Beta BV", "Live demo")]);

    [Fact]
    public async Task Slack_SendsTextWithDeepLinks_ToTheCustomersWebhook()
    {
        // Arrange
        var handler = new CapturingHandler();
        var sut = new SlackBriefingChannel(new HttpClient(handler), Base);
        var customer = Customer.Create("Acme");
        customer.ConfigureNotifications("https://hooks.slack.com/services/T/B/x", null);

        // Act
        sut.CanSend(customer).Should().BeTrue();
        await sut.SendAsync(customer, Note(customer), CancellationToken.None);

        // Assert — posted to the webhook, Slack link syntax, per-lead deep links, no double slash
        handler.Request!.RequestUri!.ToString().Should().Be("https://hooks.slack.com/services/T/B/x");
        handler.Body.Should().Contain("2 new leads");
        handler.Body.Should().Contain("https://getcoldstart.nl/app/leads/11111111-1111-1111-1111-111111111111|Acme");
        handler.Body.Should().NotContain(".nl//app");
    }

    [Fact]
    public void Slack_CannotSend_WithoutWebhook()
    {
        // Arrange
        var sut = new SlackBriefingChannel(new HttpClient(new CapturingHandler()), Base);

        // Act / Assert
        sut.CanSend(Customer.Create("Acme")).Should().BeFalse();
    }

    [Fact]
    public async Task WhatsApp_SendsViaTwilio_WithBasicAuthAndWhatsAppAddressing()
    {
        // Arrange
        var handler = new CapturingHandler();
        var twilio = Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "tok", WhatsAppFrom = "+14155238886" });
        var sut = new TwilioWhatsAppChannel(new HttpClient(handler), twilio, Base);
        var customer = Customer.Create("Acme");
        customer.ConfigureNotifications(null, "+31612345678");

        // Act
        sut.CanSend(customer).Should().BeTrue();
        await sut.SendAsync(customer, Note(customer), CancellationToken.None);

        // Assert
        handler.Request!.RequestUri!.ToString().Should().Contain("/Accounts/AC123/Messages.json");
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Basic");
        var body = System.Web.HttpUtility.UrlDecode(handler.Body!);
        body.Should().Contain("From=whatsapp:+14155238886");
        body.Should().Contain("To=whatsapp:+31612345678");
        body.Should().Contain("Beta BV");
    }

    [Fact]
    public void WhatsApp_CannotSend_WhenTwilioIsNotConfigured()
    {
        // Arrange — customer HAS a number, but the operator hasn't set Twilio creds
        var sut = new TwilioWhatsAppChannel(new HttpClient(new CapturingHandler()),
            Options.Create(new TwilioOptions()), Base);
        var customer = Customer.Create("Acme");
        customer.ConfigureNotifications(null, "+31612345678");

        // Act / Assert — soft-degrade
        sut.CanSend(customer).Should().BeFalse();
    }

    [Fact]
    public async Task WhatsApp_WithContentSid_SendsTemplateWithSingleLineVariables()
    {
        // Arrange — production mode: approved template, no freeform Body.
        var handler = new CapturingHandler();
        var twilio = Options.Create(new TwilioOptions
        { AccountSid = "AC1", AuthToken = "t", WhatsAppFrom = "+3197001", ContentSid = "HX123" });
        var sut = new TwilioWhatsAppChannel(new HttpClient(handler), twilio, Base);
        var customer = Customer.Create("Acme\nB.V.");
        customer.ConfigureNotifications(null, "+31612345678");

        // Act
        await sut.SendAsync(customer, Note(customer), CancellationToken.None);

        // Assert — template fields, no Body, variables cleaned of newlines
        var body = System.Web.HttpUtility.UrlDecode(handler.Body!);
        body.Should().Contain("ContentSid=HX123");
        body.Should().NotContain("Body=");
        body.Should().Contain("Acme, Beta BV");
        body.Should().NotContain("Acme\nB.V.");
    }
}
