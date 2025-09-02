// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

using GrhaWeb.Function.Model;

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
    public void Run([EventGridTrigger] EventGridEvent eventGridEvent)
    {
        DuesEmailEvent duesEmailEvent = new DuesEmailEvent();
        try
        {
            duesEmailEvent = eventGridEvent.Data.ToObjectFromJson<DuesEmailEvent>();
            //log.LogWarning($"Event type: {eventGridEvent.EventType}, parcelId: {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, totalDue: {duesEmailEvent.totalDue}, email: {duesEmailEvent.emailAddr}");

            var returnMessage = hoaDbCommon.CreateAndSend(duesEmailEvent);

        }
        catch (Exception ex)
        {
            log.LogError($"Exception, message: {ex.Message} {ex.StackTrace}");
            log.LogError($">>> Event type: {eventGridEvent.EventType}, parcelId: {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, totalDue: {duesEmailEvent.totalDue}, email: {duesEmailEvent.emailAddr}");
            throw new Exception(ex.Message);
        }


        /*
        function createDuesMessage($conn,$Parcel_ID) {
            $htmlMessageStr = '';
            $title = 'Member Dues Notice';
            $hoaName = getConfigValDB($conn,'hoaName');

            // Current System datetime
            $currSysDate = date_create();
            // Get the current data for the property
            $hoaRec = getHoaRec($conn,$Parcel_ID);

            $FY = 1991;
            // *** just use the highest FY - the first assessment record ***
            $result = $conn->query("SELECT MAX(FY) AS maxFY FROM hoa_assessments; ");
            if ($result->num_rows > 0) {
                while($row = $result->fetch_assoc()) {
                    $FY = $row["maxFY"];
                }
            }
            $result->close();

            $noticeYear = (string) $hoaRec->assessmentsList[0]->FY - 1;
            $noticeDate = date_format($currSysDate,"Y-m-d");

            $htmlMessageStr .= '<b>' . $hoaName . '</b>' . '<br>';
            $htmlMessageStr .= $title . " for Fiscal Year " . '<b>' . $FY . '</b>' . '<br>';
            $htmlMessageStr .= '<b>For the Period:</b> Oct 1, ' . $noticeYear . ' thru Sept 30, ' . $FY . '<br><br>';

            if (!$hoaRec->assessmentsList[0]->Paid) {
                $htmlMessageStr .= '<b>Current Dues Amount: </b>$' . stringToMoney($hoaRec->assessmentsList[0]->DuesAmt) . '<br>';
            }
            //$htmlMessageStr .= '<b>Total Outstanding (as of ' . $noticeDate . ') :</b> $' . $hoaRec->TotalDue . '<br>';
            $htmlMessageStr .= '<b>*****Total Outstanding:</b> $' . $hoaRec->TotalDue . ' (Includes fees, current & past dues)<br>';
            //$htmlMessageStr .= '*** Includes fees, current & past dues *** <br>';
            $htmlMessageStr .= '<b>Due Date: </b>' . 'October 1, ' . $noticeYear . '<br>';
            $htmlMessageStr .= '<b>Dues must be paid to avoid a lien and lien fees </b><br><br>';

            $htmlMessageStr .= '<b>Parcel Id: </b>' . $hoaRec->Parcel_ID . '<br>';
            $htmlMessageStr .= '<b>Owner: </b>' . $hoaRec->ownersList[0]->Mailing_Name . '<br>';
            $htmlMessageStr .= '<b>Location: </b>' . $hoaRec->Parcel_Location . '<br>';
            $htmlMessageStr .= '<b>Phone: </b>' . $hoaRec->ownersList[0]->Owner_Phone . '<br>';
            $htmlMessageStr .= '<b>Email: </b>' . $hoaRec->DuesEmailAddr . '<br>';
            $htmlMessageStr .= '<b>Email2: </b>' . $hoaRec->ownersList[0]->EmailAddr2 . '<br>';

            $htmlMessageStr .= '<h3><a href="' . getConfigValDB($conn,'duesUrl') . '">Click here to view Dues Statement or PAY online</a></h3>';
            $htmlMessageStr .= '*** Online payment is for properties with ONLY current dues outstanding - if there are outstanding past dues or fees on the account, contact Treasurer for online payment options *** <br>';

            $htmlMessageStr .= 'Send payment checks to:<br>';
            $htmlMessageStr .= '<b>' . getConfigValDB($conn,'hoaNameShort') . '</b>' . '<br>';
            $htmlMessageStr .= '<b>' . getConfigValDB($conn,'hoaAddress1') . '</b>' . '<br>';
            $htmlMessageStr .= '<b>' . getConfigValDB($conn,'hoaAddress2') . '</b>' . '<br>';

            $helpNotes = getConfigValDB($conn,'duesNotes');
            if (!empty($helpNotes)) {
                $htmlMessageStr .= '<br>' . $helpNotes . '<br>';
            }

            return $htmlMessageStr;
        }
        */


    }
}