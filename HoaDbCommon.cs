/*==============================================================================
(C) Copyright 2024 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Common functions to handle getting data from the data sources.
              Centralize all data source libraries and configuration to this
              utility class.
--------------------------------------------------------------------------------
Modification History
2024-11-19 JJK  Initial versions
2025-04-12 JJK  Added functions to read and update BoardOfTrustee data source
2025-04-13 JJK  *** NEW philosophy - put the error handling (try/catch) in the
                main/calling function, and leave it out of the DB Common - DB
                Common will throw any error, and the caller can log and handle
2025-05-08 JJK  Added function to convert images and upload to 
2025-05-14 JJK  Added calc of DuesDue in the assessments record
2025-05=16 JJK  Working on DuesStatement (and PDF)
2025-05-31 JJK  Added AddPatchField and functions to update hoadb property
2025-06-27 JJK  Added Assessment update
2025-08-03 JJK  Added GetSalesList and UpdateSales functions to get sales data
                and update the sales record WelcomeSent flag
2025-08-08 JJK  Added new owner update function
2025-08-17 JJK  Added functions for reports, and corrected problem with dues
                paid counts because of duplicate assessments from the sql to
                cosmosdb migration (load program has been corrected).
                Modified the assessments update to choose new owners
2025-08-21 JJK  Added function to get and update hoa_config values
--------------------------------------------------------------------------------
2025-09-01 JJK  Copied from the GrhaWeb and modified for the functions 
                needed for the GrhaFunctions
2025-09-22 JJK  Added SendPaymentEmail function
================================================================================*/
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Azure;
using Azure.Messaging.EventGrid;
using Azure.Communication.Email;

using GrhaWeb.Function.Model;

namespace GrhaWeb.Function
{
    public class HoaDbCommon
    {
        private readonly ILogger log;
        private readonly IConfiguration config;
        private readonly string? apiCosmosDbConnStr;
        private readonly string? apiStorageConnStr;
        private readonly string databaseId;
        private readonly string? grhaSendEmailEventTopicEndpoint;
        private readonly string? grhaSendEmailEventTopicKey;
        private readonly string? acsEmailConnStr;  // Your ACS Email connection string from the Azure portal
        private readonly string? acsEmailSenderAddress; 
        private readonly CommonUtil util;

        public HoaDbCommon(ILogger logger, IConfiguration configuration)
        {
            log = logger;
            config = configuration;
            apiCosmosDbConnStr = config["API_COSMOS_DB_CONN_STR"];
            apiStorageConnStr = config["API_STORAGE_CONN_STR"];
            databaseId = "hoadb";
            grhaSendEmailEventTopicEndpoint = config["GRHA_SENDMAIL_EVENT_TOPIC_ENDPOINT"];
            grhaSendEmailEventTopicKey = config["GRHA_SENDMAIL_EVENT_TOPIC_KEY"];
            acsEmailConnStr = config["ACS_EMAIL_CONN_STR"];
            acsEmailSenderAddress = config["ACS_EMAIL_SENDER_ADDRESS"];
            util = new CommonUtil(log);
        }

        public async Task<string> SendEmailandUpdateRecs(DuesEmailEvent duesEmailEvent)
        {
            string returnMessage = "";

            string containerId = "hoa_communications";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);
            DateTime currDateTime = DateTime.Now;
            string LastChangedTs = currDateTime.ToString("o");

            var hoaRec = await GetHoaRec(duesEmailEvent.parcelId);

            string subject = $"{duesEmailEvent.hoaNameShort} Dues Notice";
            string htmlMessageStr = "";
            string title = duesEmailEvent.hoaNameShort + " Member Dues Notice";    // TEST?
            string noticeYear = (hoaRec.assessmentsList[0].FY - 1).ToString();

            htmlMessageStr = $"<b>{duesEmailEvent.hoaName}</b><br>";
            htmlMessageStr += $"{title} for Fiscal Year <b>{hoaRec.assessmentsList[0].FY.ToString()}</b><br>";
            htmlMessageStr += $"<b>For the Period:</b> Oct 1, {noticeYear} thru Sept 30, {hoaRec.assessmentsList[0].FY.ToString()}<br><br>";
            if (hoaRec.assessmentsList[0].Paid != 1) {
                htmlMessageStr += $"<b>Current Dues Amount: </b>{hoaRec.assessmentsList[0].DuesAmt}<br>";
            }
            htmlMessageStr += $"<b>*****Total Outstanding:</b> ${hoaRec.totalDue} (Includes fees, current & past dues)<br>";
            htmlMessageStr += $"<b>Due Date: </b>October 1, {noticeYear}<br>";
            htmlMessageStr += $"<b>Dues must be paid to avoid a lien and lien fees </b><br><br>";

            htmlMessageStr += $"<b>Parcel Id: </b>{duesEmailEvent.parcelId}<br>";
            htmlMessageStr += $"<b>Owner: </b>{hoaRec.property.Mailing_Name}<br>";
            htmlMessageStr += $"<b>Location: </b>{hoaRec.property.Parcel_Location}<br>";
            htmlMessageStr += $"<b>Phone: </b>{hoaRec.ownersList[0].Owner_Phone}<br>";
            htmlMessageStr += $"<b>Email: </b>{hoaRec.ownersList[0].EmailAddr}<br>";
            htmlMessageStr += $"<b>Email2: </b>{hoaRec.ownersList[0].EmailAddr2}<br>";

            htmlMessageStr += $"<h3><a href='{duesEmailEvent.duesUrl}'>Click here to view Dues Statement or PAY online</a></h3>";
            htmlMessageStr += $"*** Online payment is for properties with ONLY current dues outstanding - if there are outstanding past dues or fees on the account, contact Treasurer for online payment options *** <br>";

            htmlMessageStr += $"Send payment checks to:<br>";
            htmlMessageStr += $"<b>{duesEmailEvent.hoaNameShort}</b><br>";
            htmlMessageStr += $"<b>{duesEmailEvent.hoaAddress1}</b><br>";
            htmlMessageStr += $"<b>{duesEmailEvent.hoaAddress2}</b><br>";

            if (!String.IsNullOrEmpty(duesEmailEvent.helpNotes)) {
                htmlMessageStr += $"<br>{duesEmailEvent.helpNotes}<br>";
            }


            // Create the EmailClient
            var emailClient = new EmailClient(acsEmailConnStr);

            // Build the email content
            var emailContent = new EmailContent(title)
            {
                Html = htmlMessageStr
            };

            var emailRecipients = new EmailRecipients(
                to: new List<EmailAddress>
                {
                    new EmailAddress("johnkauflin@gmail.com")   // TEST
                }
            );
                    //new EmailAddress(duesEmailEvent.emailAddr)
                    //new EmailAddress("johnkauflin@gmail.com", "John Name")   // TEST

            // Create the message
            var emailMessage = new EmailMessage(
                senderAddress: acsEmailSenderAddress, // must be from a verified domain in ACS
                content: emailContent,
                recipients: emailRecipients
            );

            // Send the email and wait until the operation completes
            EmailSendOperation operation = await emailClient.SendAsync(
                WaitUntil.Completed,
                emailMessage
            );

            // Check the result
            EmailSendResult result = operation.Value;
            if (result.Status != EmailSendStatus.Succeeded)
            {
                log.LogError("---------- DUES EMAIL SEND FAILED ------------");
                log.LogError($">>> {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, email: {duesEmailEvent.emailAddr}");
                log.LogError($"Email send status: {result.Status.ToString()}");
                throw new Exception("Dues email send failed");
            }

            //----------------------------------------------------------------------------------------------------------------
            // Update the status of the Communications record indicating that the email has been SENT
            //----------------------------------------------------------------------------------------------------------------
            // Initialize a list of PatchOperation (and default to setting the mandatory LastChanged fields)
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/SentStatus", "Y"),
                PatchOperation.Replace("/LastChangedBy", "SendMail"),
                PatchOperation.Replace("/LastChangedTs", LastChangedTs)
            };

            // Convert the list to an array
            PatchOperation[] patchArray = patchOperations.ToArray();

            ItemResponse<dynamic> response = await container.PatchItemAsync<dynamic>(
                duesEmailEvent.id,
                new PartitionKey(duesEmailEvent.parcelId),
                patchArray
            );

            returnMessage = $"Successfully sent email and updated comm rec, Parcel_ID = {duesEmailEvent.parcelId}";
            return returnMessage;
        }

        public async Task<string> SendPaymentEmail(DuesEmailEvent duesEmailEvent)
        {
            string returnMessage = "";

            string containerId = "hoa_payments";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);
            DateTime currDateTime = DateTime.UtcNow;
            string LastChangedTs = currDateTime.ToString("o");

            // Create the EmailClient
            var emailClient = new EmailClient(acsEmailConnStr);

            // Build the email content
            var emailContent = new EmailContent(duesEmailEvent.mailSubject)
            {
                Html = duesEmailEvent.htmlMessage
            };

            var emailRecipients = new EmailRecipients(
                to: new List<EmailAddress>
                {
                    new EmailAddress("johnkauflin@gmail.com")   // TEST
                }
            );
                    //new EmailAddress(duesEmailEvent.emailAddr)
                    //new EmailAddress("johnkauflin@gmail.com", "John TEST")   // TEST

            // Create the message
            var emailMessage = new EmailMessage(
                senderAddress: acsEmailSenderAddress, // must be from a verified domain in ACS
                content: emailContent,
                recipients: emailRecipients
            );

            // Send the email and wait until the operation completes
            EmailSendOperation operation = await emailClient.SendAsync(
                WaitUntil.Completed,
                emailMessage
            );

            // Check the result
            EmailSendResult result = operation.Value;
            log.LogWarning($"Email send status: {result.Status.ToString()}, Succeeded = {EmailSendStatus.Succeeded.ToString()}");
            if (result.Status != EmailSendStatus.Succeeded)
            {
                log.LogError("---------- PAYMENT EMAIL SEND FAILED ------------");
                log.LogError($">>> {duesEmailEvent.parcelId}, id: {duesEmailEvent.id}, email: {duesEmailEvent.emailAddr}");
                log.LogError($"Email send status: {result.Status.ToString()}");
                throw new Exception("Payment email send failed");
            }

            //----------------------------------------------------------------------------------------------------------------
            // Update the status of the Payment record indicating that the email has been SENT
            //----------------------------------------------------------------------------------------------------------------
            // Initialize a list of PatchOperation (and default to setting the mandatory LastChanged fields)
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/paidEmailSent", "Y"),
                PatchOperation.Replace("/LastChangedTs", LastChangedTs)
            };

            // Convert the list to an array
            PatchOperation[] patchArray = patchOperations.ToArray();

            ItemResponse<dynamic> response = await container.PatchItemAsync<dynamic>(
                duesEmailEvent.id,
                new PartitionKey(duesEmailEvent.parcelId),
                patchArray
            );

            returnMessage = $"Successfully sent email and updated payments rec, Parcel_ID = {duesEmailEvent.parcelId}";
            return returnMessage;
        }



        // Common internal function to lookup configuration values
        private async Task<string> getConfigVal(Container container, string configName)
        {
            string configVal = "";
            string sql = $"SELECT * FROM c WHERE c.ConfigName = '{configName}' ";
            var feed = container.GetItemQueryIterator<hoa_config>(sql);
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    configVal = item.ConfigValue ?? "";
                }
            }
            return configVal;
        }


        //==============================================================================================================
        // Main details lookup service to get data from all the containers for a specific property and populate
        // the HoaRec object.  It also calculates the total Dues due with interest, and gets emails and sales info
        //==============================================================================================================
        public async Task<HoaRec> GetHoaRec(string parcelId, string ownerId = "", string fy = "", string saleDate = "")
        {
            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string containerId = "hoa_properties";
            string sql = $"";

            HoaRec hoaRec = new HoaRec();
            hoaRec.totalDue = 0.00m;
            hoaRec.paymentInstructions = "";
            hoaRec.paymentFee = 0.00m;
            hoaRec.duesEmailAddr = "";

            hoaRec.ownersList = new List<hoa_owners>();
            hoaRec.assessmentsList = new List<hoa_assessments>();
            hoaRec.commList = new List<hoa_communications>();
            hoaRec.salesList = new List<hoa_sales>();
            hoaRec.totalDuesCalcList = new List<TotalDuesCalcRec>();
            hoaRec.emailAddrList = new List<string>();

            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container configContainer = db.GetContainer("hoa_config");

            //----------------------------------- Property --------------------------------------------------------
            containerId = "hoa_properties";
            Container container = db.GetContainer(containerId);
            //sql = $"SELECT * FROM c WHERE c.id = '{parcelId}' ";
            var queryDefinition = new QueryDefinition(
                "SELECT * FROM c WHERE c.id = @parcelId ")
                    .WithParameter("@parcelId", parcelId);
            var feed = container.GetItemQueryIterator<hoa_properties>(queryDefinition);
            int cnt = 0;
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    hoaRec.property = item;
                }
            }

            //----------------------------------- Owners ----------------------------------------------------------
            containerId = "hoa_owners";
            Container ownersContainer = db.GetContainer(containerId);

            if (!ownerId.Equals(""))
            {
                queryDefinition = new QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @ownerId AND c.Parcel_ID = @parcelId ")
                    .WithParameter("@ownerId", ownerId)
                    .WithParameter("@parcelId", parcelId);
            }
            else
            {
                queryDefinition = new QueryDefinition(
                    "SELECT * FROM c WHERE c.Parcel_ID = @parcelId ORDER BY c.OwnerID DESC ")
                    .WithParameter("@parcelId", parcelId);
            }
            var ownersFeed = ownersContainer.GetItemQueryIterator<hoa_owners>(queryDefinition);
            cnt = 0;
            while (ownersFeed.HasMoreResults)
            {
                var response = await ownersFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    hoaRec.ownersList.Add(item);

                    if (item.CurrentOwner == 1)
                    {
                        // Current Owner fields are already part of the properties record (including property.OwnerID)

                        hoaRec.duesEmailAddr = item.EmailAddr;
                        if (!string.IsNullOrWhiteSpace(item.EmailAddr))
                        {
                            hoaRec.emailAddrList.Add(item.EmailAddr);
                        }
                        if (!string.IsNullOrWhiteSpace(item.EmailAddr2))
                        {
                            hoaRec.emailAddrList.Add(item.EmailAddr2);
                        }
                    }
                }
            }

            //----------------------------------- Emails ----------------------------------------------------------
            containerId = "hoa_payments";
            Container paymentsContainer = db.GetContainer(containerId);
            //--------------------------------------------------------------------------------------------------
            // Override email address to use if we get the last email used to make an electronic payment
            // 10/15/2022 JJK Modified to only look for payments within the last year (because of renter issue)
            //--------------------------------------------------------------------------------------------------
            sql = $"SELECT * FROM c WHERE c.OwnerID = {hoaRec.property!.OwnerID} AND c.Parcel_ID = '{parcelId}' AND c.payment_date > DateTimeAdd('yy', -1, GetCurrentDateTime()) ";
            var paymentsFeed = paymentsContainer.GetItemQueryIterator<hoa_payments>(sql);
            cnt = 0;
            while (paymentsFeed.HasMoreResults)
            {
                var response = await paymentsFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    if (!string.IsNullOrWhiteSpace(item.payer_email))
                    {
                        // If there is an email from the last electronic payment, for the current Owner,
                        // add it to the email list (if not already in the array)
                        string compareStr = item.payer_email.ToLower();
                        if (Array.IndexOf(hoaRec.emailAddrList.ToArray(), compareStr) < 0)
                        {
                            hoaRec.emailAddrList.Add(compareStr);
                        }
                    }
                }
            }

            //----------------------------------- Assessments -----------------------------------------------------
            containerId = "hoa_assessments";
            Container assessmentsContainer = db.GetContainer(containerId);
            if (fy.Equals("") || fy.Equals("LATEST"))
            {
                sql = $"SELECT * FROM c WHERE c.Parcel_ID = '{parcelId}' ORDER BY c.FY DESC ";
            }
            else
            {
                sql = $"SELECT * FROM c WHERE c.Parcel_ID = '{parcelId}' AND c.FY = {fy} ORDER BY c.FY DESC ";
            }
            var assessmentsFeed = assessmentsContainer.GetItemQueryIterator<hoa_assessments>(sql);
            cnt = 0;
            DateTime currDate = DateTime.Now;
            DateTime dateTime;
            DateTime dateDue;
            while (assessmentsFeed.HasMoreResults)
            {
                var response = await assessmentsFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    if (fy.Equals("LATEST") && cnt > 1)
                    {
                        continue;
                    }
                    /*
                    if (item.DateDue is null)
                    {
                        dateDue = DateTime.Parse((item.FY - 1).ToString() + "-10-01");
                    }
                    else
                    {
                        dateDue = DateTime.Parse(item.DateDue);
                    }
                    */
                    dateDue = DateTime.Parse((item.FY - 1).ToString() + "-10-01");
                    item.DateDue = dateDue.ToString("yyyy-MM-dd");
                    // If you don't need the DateTime object, you can do it in 1 line
                    //item.DateDue = DateTime.Parse(item.DateDue).ToString("yyyy-MM-dd");

                    if (item.Paid == 1)
                    {
                        if (string.IsNullOrWhiteSpace(item.DatePaid))
                        {
                            item.DatePaid = item.DateDue;
                        }
                        dateTime = DateTime.Parse(item.DatePaid);
                        item.DatePaid = dateTime.ToString("yyyy-MM-dd");
                    }

                    item.DuesDue = false;
                    if (item.Paid != 1 && item.NonCollectible != 1)
                    {
                        // check dates (if NOT PAID)
                        if (currDate > dateDue)
                        {
                            item.DuesDue = true;
                        }
                    }

                    hoaRec.assessmentsList.Add(item);

                } // Assessments loop
            }

            // Pass the assessments to the common function to calculate Total Dues
            bool onlyCurrYearDue;
            decimal totalDueOut;
            hoaRec.totalDuesCalcList = util.CalcTotalDues(hoaRec.assessmentsList, out onlyCurrYearDue, out totalDueOut);
            hoaRec.totalDue = totalDueOut;

            //---------------------------------------------------------------------------------------------------
            // Construct the online payment button and instructions according to what is owed
            //---------------------------------------------------------------------------------------------------
            // Only display payment button if something is owed
            // For now, only set payment button if just the current year dues are owed (no other years or open liens)
            if (hoaRec.totalDue > 0.0m)
            {
                hoaRec.paymentInstructions = await getConfigVal(configContainer, "OfflinePaymentInstructions");
                hoaRec.paymentFee = decimal.Parse(await getConfigVal(configContainer, "paymentFee"));
                if (onlyCurrYearDue)
                {
                    hoaRec.paymentInstructions = await getConfigVal(configContainer, "OnlinePaymentInstructions");
                }
            }

            //----------------------------------- Sales -----------------------------------------------------------
            containerId = "hoa_sales";
            Container salesContainer = db.GetContainer(containerId);
            if (saleDate.Equals(""))
            {
                sql = $"SELECT * FROM c WHERE c.id = '{parcelId}' ORDER BY c.CreateTimestamp DESC ";
            }
            else
            {
                sql = $"SELECT * FROM c WHERE c.id = '{parcelId}' AND c.SALEDT = {saleDate} ";
            }
            var salesFeed = salesContainer.GetItemQueryIterator<hoa_sales>(sql);
            cnt = 0;
            while (salesFeed.HasMoreResults)
            {
                var response = await salesFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    hoaRec.salesList.Add(item);
                } // Sales loop
            }

            return hoaRec;
        }

        //==============================================================================================================
        //  Function to return an array of full hoaRec objects (with a couple of parameters to filter list)
        //==============================================================================================================
        public async Task<List<HoaRec>> GetHoaRecListDB(
            bool duesOwed = false,
            bool skipEmail = false,
            bool currYearPaid = false,
            bool currYearUnpaid = false,
            bool testEmail = false)
        {
            List<HoaRec> outputList = new List<HoaRec>();
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container propertiesContainer = db.GetContainer("hoa_properties");
            Container ownersContainer = db.GetContainer("hoa_owners");
            Container assessmentsContainer = db.GetContainer("hoa_assessments");
            //Container salesContainer = db.GetContainer("hoa_sales");
            Container configContainer = db.GetContainer("hoa_config");

            string sql = "";
            string? testEmailParcel = null;
            int fy = 0;

            if (testEmail)
            {
                // Get the test parcel from config
                testEmailParcel = await getConfigVal(configContainer, "duesEmailTestParcel");
                //sql = $"SELECT * FROM c WHERE c.Parcel_ID = '{testEmailParcel}' ORDER BY c.Parcel_ID";
            }

            // Get max FY if needed
            if (currYearPaid || currYearUnpaid)
            {
                var maxFyQuery = new QueryDefinition("SELECT VALUE MAX(c.FY) FROM c");
                var maxFyFeed = assessmentsContainer.GetItemQueryIterator<int>(maxFyQuery);
                while (fy == 0 && maxFyFeed.HasMoreResults)
                {
                    var response = await maxFyFeed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        fy = item;
                    }
                }
            }

            // Get all properties
            List<hoa_properties> propList = new List<hoa_properties>();
            var allPropQuery = new QueryDefinition("SELECT * FROM c ORDER BY c.Parcel_ID");
            var allPropFeed = propertiesContainer.GetItemQueryIterator<hoa_properties>(allPropQuery);
            while (allPropFeed.HasMoreResults)
            {
                var response = await allPropFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    propList.Add(item);
                }
            }

            // Get all current owners
            List<hoa_owners> ownerList = new List<hoa_owners>();
            var allOwnerQuery = new QueryDefinition("SELECT * FROM c WHERE c.CurrentOwner = 1");
            var allOwnerFeed = ownersContainer.GetItemQueryIterator<hoa_owners>(allOwnerQuery);
            while (allOwnerFeed.HasMoreResults)
            {
                var response = await allOwnerFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    ownerList.Add(item);
                }
            }

            // Get all assessments
            List<hoa_assessments> assessmentList = new List<hoa_assessments>();
            var allAssessmentQuery = new QueryDefinition("SELECT * FROM c ORDER BY c.FY DESC ");
            if (currYearPaid)
            {
                allAssessmentQuery = new QueryDefinition("SELECT * FROM c WHERE c.FY = @fy AND c.Paid = 1")
                    .WithParameter("@fy", fy);
            }
            if (currYearUnpaid)
            {
                allAssessmentQuery = new QueryDefinition(
                    "SELECT * FROM c WHERE c.FY = @fy AND c.Paid = 0 AND (IS_NULL(c.NonCollectible) OR c.NonCollectible != 1)")
                    .WithParameter("@fy", fy);
            }
            var allAssessmentFeed = assessmentsContainer.GetItemQueryIterator<hoa_assessments>(allAssessmentQuery);
            while (allAssessmentFeed.HasMoreResults)
            {
                var response = await allAssessmentFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    assessmentList.Add(item);
                }
            }

            // Get all config values into a dictionary
            /*
            Dictionary<string, string> configDict = new Dictionary<string, string>();
            var configQuery = new QueryDefinition("SELECT * FROM c");
            var configFeed = configContainer.GetItemQueryIterator<hoa_config>(configQuery);
            while (configFeed.HasMoreResults)
            {
                var response = await configFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    if (!string.IsNullOrEmpty(item.ConfigName))
                        configDict[item.ConfigName] = item.ConfigValue ?? string.Empty;
                }
            }
            */

            // Build HoaRec for each property using the in-memory lists and config
            foreach (var prop in propList)
            {
                //var hoaRec = BuildHoaRecFromLists(prop, ownerList, assessmentList, configDict);
                var hoaRec = BuildHoaRecFromLists(prop, ownerList, assessmentList);

                if ((duesOwed || currYearUnpaid) && hoaRec.totalDue < 0.01m)
                {
                    continue;
                }
                if (skipEmail && (hoaRec.property.UseEmail == 1))
                {
                    continue;
                }
                outputList.Add(hoaRec);
            }

            return outputList;
        }

        // Build an HoaRec using in-memory lists of owners and assessments
        public HoaRec BuildHoaRecFromLists(
            hoa_properties property,
            List<hoa_owners> ownerList,
            List<hoa_assessments> assessmentList)
        //            Dictionary<string, string> configDict)
        {
            HoaRec hoaRec = new HoaRec();
            hoaRec.property = property;
            hoaRec.ownersList = ownerList.Where(o => o.Parcel_ID == property.Parcel_ID).ToList();
            hoaRec.assessmentsList = assessmentList.Where(a => a.Parcel_ID == property.Parcel_ID).ToList();
            hoaRec.totalDuesCalcList = util.CalcTotalDues(hoaRec.assessmentsList, out bool onlyCurrYearDue, out decimal totalDueOut);
            hoaRec.totalDue = totalDueOut;
            // Set config-based fields if present
            /*
            if (configDict != null)
            {
                //if (configDict.TryGetValue("OfflinePaymentInstructions", out var offlinePay))
                //    hoaRec.paymentInstructions = offlinePay;
                //if (configDict.TryGetValue("OnlinePaymentInstructions", out var onlinePay) && onlyCurrYearDue)
                //    hoaRec.paymentInstructions = onlinePay;
                //if (configDict.TryGetValue("paymentFee", out var payFee) && decimal.TryParse(payFee, out var feeVal))
                //    hoaRec.paymentFee = feeVal;
                if (configDict.TryGetValue("duesStatementNotes", out var notes))
                    hoaRec.duesStatementNotes = notes;
                if (configDict.TryGetValue("hoaNameShort", out var hoaName))
                    hoaRec.hoaNameShort = hoaName;
            }
            */
            return hoaRec;
        }


        public async Task<List<hoa_communications>> GetCommunicationsDB(string parcelId)
        {
            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string containerId = "hoa_communications";
            //string sql = $"";

            List<hoa_communications> hoaCommunicationsList = new List<hoa_communications>();

            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);

            QueryDefinition queryDefinition;
            if (parcelId.Equals("DuesNoticeEmails"))
            {
                queryDefinition = new QueryDefinition(
                    //"SELECT * FROM c WHERE c.Email = 1 AND c.SentStatus = 'Y' ORDER BY c._ts DESC OFFSET 0 LIMIT 200");
                    "SELECT * FROM c WHERE c.Email = 1 ORDER BY c._ts DESC OFFSET 0 LIMIT 200");
            }
            else
            {
                //    "SELECT * FROM c WHERE c.Parcel_ID = @parcelId ORDER BY c.CommID DESC ")
                queryDefinition = new QueryDefinition(
                    "SELECT * FROM c WHERE c.Parcel_ID = @parcelId ORDER BY c._ts DESC ")
                    .WithParameter("@parcelId", parcelId);
            }

            var feed = container.GetItemQueryIterator<hoa_communications>(queryDefinition);
            int cnt = 0;
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    hoaCommunicationsList.Add(item);
                }
            }

            return hoaCommunicationsList;
        }

        public async Task<int> CreateDuesNoticeEmailsDB(string userName)
        {
            bool duesOwed = true;
            bool skipEmail = false;
            bool currYearPaid = false;
            bool currYearUnpaid = false;
            bool testEmail = false;
            int returnCnt = 0;

            // Get a list of the parcels that have dues owed
            var hoaRecList = await GetHoaRecListDB(duesOwed, skipEmail, currYearPaid, currYearUnpaid, testEmail);
            string containerId = "hoa_communications";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);
            DateTime currDateTime = DateTime.Now;
            string LastChangedTs = currDateTime.ToString("o");

            var eventGridPublisherClient = new EventGridPublisherClient(
                new Uri(grhaSendEmailEventTopicEndpoint),
                new AzureKeyCredential(grhaSendEmailEventTopicKey)
            );

            int cnt = 0;
            string commId = "";
            foreach (var hoaRec in hoaRecList)
            {
                hoaRec.emailAddrList = new List<string>();

                // Add the valid emails to the list
                if (!string.IsNullOrWhiteSpace(hoaRec.ownersList[0].EmailAddr))
                {
                    if (util.IsValidEmail(hoaRec.ownersList[0].EmailAddr))
                    {
                        hoaRec.emailAddrList.Add(hoaRec.ownersList[0].EmailAddr);
                    }
                }
                if (!string.IsNullOrWhiteSpace(hoaRec.ownersList[0].EmailAddr2))
                {
                    if (util.IsValidEmail(hoaRec.ownersList[0].EmailAddr2))
                    {
                        hoaRec.emailAddrList.Add(hoaRec.ownersList[0].EmailAddr2);
                    }
                }

                // Skip parcel if there are no valid email addresses
                if (hoaRec.emailAddrList.Count < 1)
                {
                    continue;
                }

                // Create a communication record and an email send event for each valid email address for the Owner
                foreach (var emailAddr in hoaRec.emailAddrList)
                {
                    returnCnt++;
                    //log.LogWarning($"{returnCnt} Parcel = {hoaRec.property.Parcel_ID}, TotalDue = {hoaRec.totalDue}, email = {emailAddr}");

                    commId = Guid.NewGuid().ToString();

                    // Create a metadata object from the media file information
                    hoa_communications hoa_comm = new hoa_communications
                    {
                        id = commId,
                        Parcel_ID = hoaRec.property.Parcel_ID,
                        CommID = 9999,
                        CreateTs = currDateTime,
                        OwnerID = hoaRec.property.OwnerID,
                        CommType = "Dues Notice",
                        CommDesc = "Sent to Owner email",
                        Mailing_Name = hoaRec.property.Mailing_Name,
                        Email = 1,
                        EmailAddr = emailAddr,
                        SentStatus = "N",
                        LastChangedBy = userName,
                        LastChangedTs = currDateTime
                    };

                    // Insert a new doc, or update an existing one
                    await container.CreateItemAsync(hoa_comm, new PartitionKey(hoa_comm.Parcel_ID));
                    
                    await eventGridPublisherClient.SendEventAsync(
                        new EventGridEvent(
                            subject: "DuesEmailRequest",
                            eventType: "SendMail",
                            dataVersion: "1.0",
                            //data: hoa_comm.Parcel_ID
                            data: new {id = commId, parcelId = hoa_comm.Parcel_ID, totalDue = hoaRec.totalDue, emailAddr = emailAddr}
                        )
                    );
                }
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
            return returnCnt;
        }


        // Get all config values from hoa_config container
        public async Task<List<hoa_config>> GetConfigListDB()
        {
            List<hoa_config> configList = new List<hoa_config>();
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container configContainer = db.GetContainer("hoa_config");
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.ConfigName");
            var feed = configContainer.GetItemQueryIterator<hoa_config>(query);
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    configList.Add(item);
                }
            }
            return configList;
        }



        public void AddPatchField(List<PatchOperation> patchOperations, Dictionary<string, string> formFields, string fieldName, string fieldType = "Text", string operationType = "Replace")
        {
            if (patchOperations == null || formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return; // Prevent potential null reference errors

            if (operationType.Equals("Replace", StringComparison.OrdinalIgnoreCase))
            {
                if (fieldType.Equals("Text"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, value));
                    }
                }
                else if (fieldType.Equals("Int"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, int.Parse(value)));
                    }
                }
                else if (fieldType.Equals("Money"))
                {
                    string value = formFields[fieldName]?.Trim() ?? string.Empty;
                    //string input = "$1,234.56";
                    if (decimal.TryParse(value, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out decimal moneyVal))
                    {
                        Console.WriteLine($"Parsed currency: {moneyVal}");
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, moneyVal));
                    }
                }
                else if (fieldType.Equals("Bool"))
                {
                    int value = 0;
                    if (formFields.ContainsKey(fieldName))
                    {
                        string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                        if (checkedValue.Equals("on"))
                        {
                            value = 1;
                        }
                    }
                    patchOperations.Add(PatchOperation.Replace("/" + fieldName, value));
                }
            }
            else if (operationType.Equals("Add", StringComparison.OrdinalIgnoreCase))
            {
                //string value = formFields[fieldName]?.Trim() ?? string.Empty;
                //patchOperations.Add(PatchOperation.Add("/" + fieldName, value));

                if (fieldType.Equals("Text"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Add("/" + fieldName, value));
                    }
                }
                else if (fieldType.Equals("Int"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Add("/" + fieldName, int.Parse(value)));
                    }
                }
                else if (fieldType.Equals("Bool"))
                {
                    int value = 0;
                    if (formFields.ContainsKey(fieldName))
                    {
                        string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                        if (checkedValue.Equals("on"))
                        {
                            value = 1;
                        }
                    }
                    patchOperations.Add(PatchOperation.Add("/" + fieldName, value));
                }
            }
            else if (operationType.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                patchOperations.Add(PatchOperation.Remove("/" + fieldName));
            }
        }


        public T GetFieldValue<T>(Dictionary<string, string> formFields, string fieldName, T defaultValue = default)
        {
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return defaultValue;

            if (formFields.TryGetValue(fieldName, out string rawValue))
            {
                try
                {
                    if (typeof(T) == typeof(bool))
                    {
                        object boolValue = rawValue.Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                        return (T)boolValue;
                    }
                    else
                    {
                        return (T)Convert.ChangeType(rawValue.Trim(), typeof(T));
                    }
                }
                catch
                {
                    // Optionally log the error here
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public int GetFieldValueBool(Dictionary<string, string> formFields, string fieldName)
        {
            int value = 0;
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return value; // Prevent potential null reference errors

            if (formFields.ContainsKey(fieldName))
            {
                string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                if (checkedValue.Equals("on"))
                {
                    value = 1;
                }
            }
            return value;
        }
        public decimal GetFieldValueMoney(Dictionary<string, string> formFields, string fieldName)
        {
            decimal value = 0.00m;
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return value; // Prevent potential null reference errors

            if (formFields.ContainsKey(fieldName))
            {
                string rawValue = formFields[fieldName]?.Trim() ?? string.Empty;
                //string input = "$1,234.56";
                if (decimal.TryParse(rawValue, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out decimal moneyVal))
                {
                    //Console.WriteLine($"Parsed currency: {moneyVal}");
                }
                value = moneyVal;
            }
            return value;
        }


        public async Task UpdatePropertyDB(string userName, Dictionary<string, string> formFields)
        {
            DateTime currDateTime = DateTime.Now;
            string LastChangedTs = currDateTime.ToString("o");

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "hoadb";
            string containerId = "hoa_properties";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);

            //foreach (var field in formFields)
            //{
            //    log.LogWarning($">>> in DB, Field {field.Key}: {field.Value}");
            //}
            string parcelId = formFields["Parcel_ID"].Trim();

            // Initialize a list of PatchOperation (and default to setting the mandatory LastChanged fields)
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/LastChangedBy", userName),
                PatchOperation.Replace("/LastChangedTs", LastChangedTs)
            };

            AddPatchField(patchOperations, formFields, "UseEmail", "Bool");
            AddPatchField(patchOperations, formFields, "Comments");

            // Convert the list to an array
            PatchOperation[] patchArray = patchOperations.ToArray();

            ItemResponse<dynamic> response = await container.PatchItemAsync<dynamic>(
                parcelId,
                new PartitionKey(parcelId),
                patchArray
            );
        }


        public async Task<hoa_owners> UpdateOwnerDB(string userName, Dictionary<string, string> formFields)
        {
            DateTime currDateTime = DateTime.Now;
            string LastChangedTs = currDateTime.ToString("o");
            hoa_owners ownerRec = null;

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "hoadb";
            string containerId = "hoa_owners";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);

            string parcelId = formFields["Parcel_ID"].Trim();
            string ownerId = formFields["OwnerID"].Trim();

            // Initialize a list of PatchOperation
            List<PatchOperation> patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/LastChangedBy", userName),
                PatchOperation.Replace("/LastChangedTs", LastChangedTs)
            };

            //AddPatchField(patchOperations, formFields, "CurrentOwner", "Bool");
            AddPatchField(patchOperations, formFields, "Owner_Name1");
            AddPatchField(patchOperations, formFields, "Owner_Name2");
            AddPatchField(patchOperations, formFields, "DatePurchased");
            AddPatchField(patchOperations, formFields, "Mailing_Name");
            AddPatchField(patchOperations, formFields, "Owner_Phone");
            AddPatchField(patchOperations, formFields, "EmailAddr");
            AddPatchField(patchOperations, formFields, "EmailAddr2");
            AddPatchField(patchOperations, formFields, "Comments");

            // Convert the list to an array
            PatchOperation[] patchArray = patchOperations.ToArray();

            ItemResponse<dynamic> response = await container.PatchItemAsync<dynamic>(
                ownerId,
                new PartitionKey(parcelId),
                patchArray
            );

            //-----------------------------------------------------------------------------------            
            // 2nd set of updates
            patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/LastChangedBy", userName),
                PatchOperation.Replace("/LastChangedTs", LastChangedTs)
            };

            AddPatchField(patchOperations, formFields, "AlternateMailing", "Bool");
            AddPatchField(patchOperations, formFields, "Alt_Address_Line1");
            AddPatchField(patchOperations, formFields, "Alt_Address_Line2");
            AddPatchField(patchOperations, formFields, "Alt_City");
            AddPatchField(patchOperations, formFields, "Alt_State");
            AddPatchField(patchOperations, formFields, "Alt_Zip");

            patchArray = patchOperations.ToArray();

            response = await container.PatchItemAsync<dynamic>(
                ownerId,
                new PartitionKey(parcelId),
                patchArray
            );

            // Get the updated owner record for the return value (for display in UI)
            containerId = "hoa_owners";
            Container ownersContainer = db.GetContainer(containerId);
            var queryDefinition = new QueryDefinition(
                "SELECT * FROM c WHERE c.id = @ownerId AND c.Parcel_ID = @parcelId ")
                .WithParameter("@ownerId", ownerId)
                .WithParameter("@parcelId", parcelId);
            var ownersFeed = ownersContainer.GetItemQueryIterator<hoa_owners>(queryDefinition);
            while (ownersFeed.HasMoreResults)
            {
                var ownersResponse = await ownersFeed.ReadNextAsync();
                foreach (var item in ownersResponse)
                {
                    ownerRec = item;
                }
            }

            // if current owner, update the OWNER fields in the hoa_properties record
            if (ownerRec.CurrentOwner == 1)
            {
                containerId = "hoa_properties";
                container = db.GetContainer(containerId);

                // Initialize a list of PatchOperation (and default to setting the mandatory LastChanged fields)
                patchOperations = new List<PatchOperation>
                {
                };

                AddPatchField(patchOperations, formFields, "Owner_Name1");
                AddPatchField(patchOperations, formFields, "Owner_Name2");
                AddPatchField(patchOperations, formFields, "Mailing_Name");
                AddPatchField(patchOperations, formFields, "Owner_Phone");
                AddPatchField(patchOperations, formFields, "Alt_Address_Line1");

                // Convert the list to an array
                patchArray = patchOperations.ToArray();

                response = await container.PatchItemAsync<dynamic>(
                    parcelId,
                    new PartitionKey(parcelId),
                    patchArray
                );
            }

            return ownerRec;
        }


        public async Task<hoa_assessments> UpdateAssessmentDB(string userName, Dictionary<string, string> formFields)
        {
            DateTime currDateTime = DateTime.Now;
            string LastChangedTs = currDateTime.ToString("o");
            hoa_assessments assessmentRec = new hoa_assessments();

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "hoadb";
            string containerId = "hoa_assessments";
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);

            string parcelId = formFields["Parcel_ID"].Trim();
            string assessmentId = formFields["AssessmentId"].Trim();

            assessmentRec = await container.ReadItemAsync<hoa_assessments>(assessmentId, new PartitionKey(parcelId));

            assessmentRec.OwnerID = GetFieldValue<int>(formFields, "OwnerID", assessmentRec.OwnerID);
            assessmentRec.DuesAmt = GetFieldValueMoney(formFields, "DuesAmt").ToString("");
            //assessmentRec.DateDue = GetFieldValue<string>(formFields, "DateDue");  // Can't change this in update
            assessmentRec.Paid = GetFieldValueBool(formFields, "Paid");
            assessmentRec.NonCollectible = GetFieldValueBool(formFields, "NonCollectible");
            assessmentRec.DatePaid = GetFieldValue<string>(formFields, "DatePaid");
            assessmentRec.PaymentMethod = GetFieldValue<string>(formFields, "PaymentMethod");
            assessmentRec.Lien = GetFieldValueBool(formFields, "Lien");
            assessmentRec.LienRefNo = GetFieldValue<string>(formFields, "LienRefNo");
            assessmentRec.DateFiled = GetFieldValue<DateTime>(formFields, "DateFiled");
            assessmentRec.Disposition = GetFieldValue<string>(formFields, "Disposition");
            assessmentRec.FilingFee = GetFieldValueMoney(formFields, "FilingFee");
            assessmentRec.ReleaseFee = GetFieldValueMoney(formFields, "ReleaseFee");
            assessmentRec.DateReleased = GetFieldValue<DateTime>(formFields, "DateReleased");
            assessmentRec.LienDatePaid = GetFieldValue<DateTime>(formFields, "LienDatePaid");
            assessmentRec.AmountPaid = GetFieldValueMoney(formFields, "AmountPaid");
            assessmentRec.StopInterestCalc = GetFieldValueBool(formFields, "StopInterestCalc");
            assessmentRec.FilingFeeInterest = GetFieldValueMoney(formFields, "FilingFeeInterest");
            assessmentRec.AssessmentInterest = GetFieldValueMoney(formFields, "AssessmentInterest");
            assessmentRec.InterestNotPaid = GetFieldValueBool(formFields, "InterestNotPaid");
            assessmentRec.BankFee = GetFieldValueMoney(formFields, "BankFee");
            assessmentRec.LienComment = GetFieldValue<string>(formFields, "LienComment");
            assessmentRec.Comments = GetFieldValue<string>(formFields, "Comments");
            assessmentRec.LastChangedBy = userName;
            assessmentRec.LastChangedTs = currDateTime;

            await container.ReplaceItemAsync(assessmentRec, assessmentRec.id, new PartitionKey(assessmentRec.Parcel_ID));

            return assessmentRec;
        }


    } // public class HoaDbCommon
} // namespace GrhaWeb.Function

