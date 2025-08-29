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
        _logger.LogInformation("Event type: {type}, Event subject: {subject}", eventGridEvent.EventType, eventGridEvent.Subject);
    }
}