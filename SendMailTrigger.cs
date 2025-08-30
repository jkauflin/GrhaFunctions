// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using System;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GrhaWeb.Function;

public class SendMailTrigger
{
    private readonly ILogger<SendMailTrigger> _logger;

    public SendMailTrigger(ILogger<SendMailTrigger> logger)
    {
        _logger = logger;
    }

    [Function(nameof(SendMailTrigger))]
    //public void Run([EventGridTrigger] CloudEvent cloudEvent)
    public void Run([EventGridTrigger] EventGridEvent eventGridEvent)
    {
        //_logger.LogInformation("Event type: {type}, Event subject: {subject}", cloudEvent.Type, cloudEvent.Subject);
        _logger.LogWarning("Event type: {type}, Event subject: {subject}, Data: {data}", eventGridEvent.EventType, eventGridEvent.Subject, eventGridEvent.Data.ToString());

/*
using Azure.Communication.Email;

[ApiController]
[Route("api/[controller]")]
public class EmailController : ControllerBase
{
    private readonly EmailClient _emailClient;

    public EmailController(IConfiguration config)
    {
        string connectionString = config["ACS:EmailConnectionString"];
        _emailClient = new EmailClient(connectionString);
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendEmail([FromBody] EmailRequest request)
    {
        var emailMessage = new EmailMessage(
            senderAddress: "noreply@yourdomain.com",
            recipientAddress: request.To,
            subject: request.Subject,
            body: request.Body
        );

        await _emailClient.SendAsync(emailMessage);
        return Ok("Email sent successfully.");
    }
}

public class EmailRequest
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
}
*/




    }
}