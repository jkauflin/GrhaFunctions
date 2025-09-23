// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

using GrhaWeb.Function.Model;
using System.Threading.Tasks;

namespace GrhaWeb.Function;

public class SendMailTrigger
{
    private readonly ILogger<SendMailTrigger> log;
    private readonly IConfiguration config;
    private readonly CommonUtil util;
    private readonly HoaDbCommon hoaDbCommon;

    public SendMailTrigger(ILogger<SendMailTrigger> logger, IConfiguration configuration)
    {
        log = logger;
        config = configuration;
        util = new CommonUtil(log);
        hoaDbCommon = new HoaDbCommon(log, config);
    }

    [Function("SendMailTrigger")]
    public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent)
    {
        DuesEmailEvent duesEmailEvent = new DuesEmailEvent();
        try
        {
            string returnMessage = string.Empty;
            // De-serialize the JSON string from the Event into the DuesEmailEvent object
            duesEmailEvent = eventGridEvent.Data.ToObjectFromJson<DuesEmailEvent>();
            //log.LogWarning($"{eventGridEvent.EventType}, parcelId: {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, totalDue: {duesEmailEvent.totalDue}, email: {duesEmailEvent.emailAddr}");
            //log.LogWarning($">>> duesEmailEvent = {duesEmailEvent.ToString()}");

            bool paymentEmail = false;
            if (!string.IsNullOrEmpty(duesEmailEvent.mailType))
            {
                if (duesEmailEvent.mailType.Equals("Payment"))
                {
                    paymentEmail = true;
                }
            }

            if (paymentEmail)
            {
                returnMessage = await hoaDbCommon.SendPaymentEmail(duesEmailEvent);
            }
            else
            {
                returnMessage = await hoaDbCommon.SendEmailandUpdateRecs(duesEmailEvent);
            }

            log.LogWarning(returnMessage);
        }
        catch (Exception ex)
        {
            log.LogError("---------- DUES EMAIL FAILED ------------");
            log.LogError($">>> {eventGridEvent.EventType}, parcelId: {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, email: {duesEmailEvent.emailAddr}, type: {duesEmailEvent.mailType}");
            log.LogError($"Exception, message: {ex.Message} {ex.StackTrace}");
            throw new Exception(ex.Message);
        }

    }
}