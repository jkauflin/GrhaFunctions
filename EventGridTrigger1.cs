// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using System;
using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GrhaWeb.Function;

public class EventGridTrigger1
{
    private readonly ILogger<EventGridTrigger1> _logger;

    public EventGridTrigger1(ILogger<EventGridTrigger1> logger)
    {
        _logger = logger;
    }

    [Function(nameof(EventGridTrigger1))]
    public void Run([EventGridTrigger] CloudEvent cloudEvent)
    {
        _logger.LogInformation("Event type: {type}, Event subject: {subject}", cloudEvent.Type, cloudEvent.Subject);
    }
}