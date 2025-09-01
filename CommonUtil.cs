/*==============================================================================
(C) Copyright 2024 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Common utility functions for Web API's
--------------------------------------------------------------------------------
Modification History
2024-08-28 JJK  Initial version (converted from older PHP code)
2024-11-04 JJK  Finished stringToMoney and CalcCompoundInterest
2024-11-20 JJK  Added CalcTotalDues as a common function for doing the 
                calculation of total dues (& fees) based on Assessments
2025-08-17 JJK  Added a better version of stringToMoney that allows for currency symbols, 
                thousands separators, and decimal points
2025-08-27 JJK  Added IsValidEmail to check email addresses
================================================================================*/

using System.Net.Mail;
using System.Globalization;
using Microsoft.Extensions.Logging;

using GrhaWeb.Function.Model;

namespace GrhaWeb.Function
{
    public class CommonUtil
    {
        private readonly ILogger log;
        public CommonUtil(ILogger logger)
        {
            log = logger;
        }

        public bool IsValidEmail(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email.Trim();
            }
            catch
            {
                return false;
            }
        }
        
        public decimal stringToMoney(string moneyString)
    {
        /*
        // Remove the dollar sign and parse the string to a decimal
        decimal moneyValue = decimal.Parse(moneyString.TrimStart('$'));
        // Round down to 2 decimal places
        moneyValue = Math.Floor(moneyValue * 100) / 100;
        return moneyValue;
        */

        // Allow currency symbols, thousands separators, and decimal points
        var style = NumberStyles.Currency;
        var culture = CultureInfo.GetCultureInfo("en-US");

        if (Decimal.TryParse(moneyString, style, culture, out decimal result))
        {
            Console.WriteLine($"Parsed value: {result}"); // Output: 1234.56
        }
        else
        {
            throw new FormatException("Invalid money format, moneyString: " + moneyString);
        }
        return result;
    }

        /*
                decimal amount = 1234.56m;
                string formattedAmount = amount.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        */

        /*
            //Replace every ascii character except decimal and digits with a null, and round to 2 decimal places
            var nonMoneyCharsStr = "[\x01-\x2D\x2F\x3A-\x7F]";
            //"g" global so it does more than 1 substitution
            var regexNonMoneyChars = new RegExp(nonMoneyCharsStr, "g");
            function formatMoney(inAmount) {
                var inAmountStr = '' + inAmount;
                inAmountStr = inAmountStr.replace(regexNonMoneyChars, '');
                return parseFloat(inAmountStr).toFixed(2);
            }
        */

        //---------------------------------------------------------------------------------------------------
        // Calculate the total dues amount from the property assessments information and pass back a list
        // of the individual charge amounts and description
        //---------------------------------------------------------------------------------------------------
        public List<TotalDuesCalcRec> CalcTotalDues(List<hoa_assessments> assessmentsList,
                                                    out bool onlyCurrYearDue,
                                                    out decimal totalDue)
        {

            List<TotalDuesCalcRec> totalDuesCalcList = new List<TotalDuesCalcRec>();
            onlyCurrYearDue = false;
            totalDue = 0.00m;

            DateTime currDate = DateTime.Now;
            DateTime dateDue;
            string duesAmtStr;
            bool duesDue;
            decimal duesAmt = 0.0m;
            decimal totalLateFees;
            int monthsApart;
            int prevFY;
            string dispositionStr;
            TotalDuesCalcRec totalDuesCalcRec;
            int cnt = 0;

            // Loop through the property assessments
            foreach (var assessmentRec in assessmentsList)
            {
                cnt++;
                string tempDateDue = assessmentRec.DateDue.Split(' ')[0];
                dateDue = DateTime.Parse(tempDateDue);
                duesDue = false;

                // If NOT PAID (and still able to be collected)
                if (assessmentRec.Paid != 1 && assessmentRec.NonCollectible != 1)
                {
                    if (cnt == 1)
                    {
                        onlyCurrYearDue = true;
                    }
                    else
                    {
                        onlyCurrYearDue = false;
                    }

                    // check dates (if NOT PAID)
                    if (currDate > dateDue)
                    {
                        duesDue = true;
                    }

                    duesAmtStr = assessmentRec.DuesAmt ?? "";
                    duesAmt = stringToMoney(duesAmtStr);

                    totalDue += duesAmt;

                    totalDuesCalcRec = new TotalDuesCalcRec();
                    totalDuesCalcRec.calcDesc = "FY " + assessmentRec.FY.ToString() + " Assessment (due " + tempDateDue + ")";
                    totalDuesCalcRec.calcValue = duesAmt.ToString();
                    totalDuesCalcList.Add(totalDuesCalcRec);

                    //================================================================================================================================
                    // 2024-11-01 JJK - Updated Total Due logic according to changes specified by GRHA Board and new Treasurer
                    //      Remove the restriction of an Open Lien to adding the interest on the unpaid assessment - it will now start adding
                    //      interest when unpaid and past the DUE DATE.
                    //      In addition, a $10 a month late fee will be added to any unpaid assessments
                    //          *** Starting on 11/1/2024, Do it for every unpaid assessment (per year) for number of months from 11/1/FY-1
                    //          FY > 2024
                    //          if months > 10, use 10 ($100) - show a LATE FEE for every unpaid assessment
                    //================================================================================================================================
                    //if ($hoaAssessmentRec->Lien && $hoaAssessmentRec->Disposition == 'Open') {
                    if (duesDue)
                    {
                        // Assume that if past due, there will be interest and fees (so can't just pay the curr year due)
                        onlyCurrYearDue = false;
                        // If still calculating interest dynamically calculate the compound interest (else just use the value from the assessment record)
                        if (assessmentRec.StopInterestCalc != 1)
                        {
                            assessmentRec.AssessmentInterest = CalcCompoundInterest(duesAmt, dateDue);
                        }

                        totalDue += assessmentRec.AssessmentInterest;

                        totalDuesCalcRec = new TotalDuesCalcRec();
                        totalDuesCalcRec.calcDesc = "%6 Interest on FY " + assessmentRec.FY.ToString() + " Assessment (since " + tempDateDue + ")";
                        totalDuesCalcRec.calcValue = assessmentRec.AssessmentInterest.ToString();
                        totalDuesCalcList.Add(totalDuesCalcRec);

                        // Calculate monthly late fees (starting in November 2024 for the FY 2025)
                        if (assessmentRec.FY > 2024)
                        {
                            // number of months between the due date and current date
                            monthsApart = ((currDate.Year - dateDue.Year) * 12) + currDate.Month - dateDue.Month;
                            // Ensure the number of months is non-negative
                            monthsApart = Math.Abs(monthsApart);
                            if (monthsApart > 10)
                            {
                                monthsApart = 10;
                            }

                            totalLateFees = 10.00m * monthsApart;
                            totalDue += totalLateFees;

                            prevFY = assessmentRec.FY - 1;
                            totalDuesCalcRec = new TotalDuesCalcRec();
                            totalDuesCalcRec.calcDesc = "$10 a Month late fee on FY " + assessmentRec.FY.ToString() + " Assessment (since " + prevFY.ToString() + "-10-31)";
                            totalDuesCalcRec.calcValue = totalLateFees.ToString();
                            totalDuesCalcList.Add(totalDuesCalcRec);
                        }

                    } // if (duesDue) {

                } // if (assessmentRec.Paid != 1 && assessmentRec.NonCollectible != 1) {

                // If the Assessment was Paid but the interest was not, then add the interest to the total
                if (assessmentRec.Paid == 1 && assessmentRec.InterestNotPaid == 1)
                {
                    onlyCurrYearDue = false;

                    // If still calculating interest dynamically calculate the compound interest
                    if (assessmentRec.StopInterestCalc != 1)
                    {
                        assessmentRec.AssessmentInterest = CalcCompoundInterest(duesAmt, dateDue);
                    }

                    totalDue += assessmentRec.AssessmentInterest;

                    totalDuesCalcRec = new TotalDuesCalcRec();
                    totalDuesCalcRec.calcDesc = "%6 Interest on FY " + assessmentRec.FY.ToString() + " Assessment (since " + tempDateDue + ")";
                    totalDuesCalcRec.calcValue = assessmentRec.AssessmentInterest.ToString();
                    totalDuesCalcList.Add(totalDuesCalcRec);
                } //  if (assessmentRec.Paid == 1 && assessmentRec.InterestNotPaid == 1) {

                // If there is an Open Lien (not Paid, Released, or Closed)
                dispositionStr = assessmentRec.Disposition ?? "";
                if (assessmentRec.Lien == 1 && dispositionStr.Equals("Open") && assessmentRec.NonCollectible != 1)
                {
                    // calc interest - start date   WHEN TO CALC INTEREST
                    // unpaid fee amount and interest since the Filing Date

                    // if there is a Filing Fee (on an Open Lien), then check to calc interest (or use stored value)
                    if (assessmentRec.FilingFee > 0.0m)
                    {
                        totalDue += assessmentRec.FilingFee;
                        totalDuesCalcRec = new TotalDuesCalcRec();
                        totalDuesCalcRec.calcDesc = "FY " + assessmentRec.FY.ToString() + " Assessment Lien Filing Fee";
                        totalDuesCalcRec.calcValue = assessmentRec.FilingFee.ToString();
                        totalDuesCalcList.Add(totalDuesCalcRec);

                        // If still calculating interest dynamically calculate the compound interest
                        if (assessmentRec.StopInterestCalc != 1)
                        {
                            assessmentRec.FilingFeeInterest = CalcCompoundInterest(assessmentRec.FilingFee, assessmentRec.DateFiled);
                        }

                        totalDue += assessmentRec.FilingFeeInterest;
                        totalDuesCalcRec = new TotalDuesCalcRec();
                        totalDuesCalcRec.calcDesc = "%6 Interest on Filing Fees (since " + assessmentRec.DateFiled.ToString("yyyy-MM-dd") + ")";
                        totalDuesCalcRec.calcValue = assessmentRec.FilingFeeInterest.ToString();
                        totalDuesCalcList.Add(totalDuesCalcRec);
                    }

                    if (assessmentRec.ReleaseFee > 0.0m)
                    {
                        totalDue += assessmentRec.ReleaseFee;
                        totalDuesCalcRec = new TotalDuesCalcRec();
                        totalDuesCalcRec.calcDesc = "FY " + assessmentRec.FY.ToString() + " Assessment Lien Release Fee";
                        totalDuesCalcRec.calcValue = assessmentRec.ReleaseFee.ToString();
                        totalDuesCalcList.Add(totalDuesCalcRec);
                    }

                    if (assessmentRec.BankFee > 0.0m)
                    {
                        totalDue += assessmentRec.BankFee;
                        totalDuesCalcRec = new TotalDuesCalcRec();
                        totalDuesCalcRec.calcDesc = "FY " + assessmentRec.FY.ToString() + " Assessment Bank Fee";
                        totalDuesCalcRec.calcValue = assessmentRec.BankFee.ToString();
                        totalDuesCalcList.Add(totalDuesCalcRec);
                    }
                } // if (assessmentRec.Lien == 1 && dispositionStr.Equals("Open") && assessmentRec.NonCollectible != 1) {

            } // foreach (var assessmentRec in assessmentsList)

            return totalDuesCalcList;
        }

        public decimal CalcCompoundInterest(decimal principal, DateTime startDate) {
			/*
				 A = the future value of the investment/loan, including interest
				 P = the principal investment amount (the initial deposit or loan amount)
				 r = the annual interest rate (decimal)
				 n = the number of times that interest is compounded per year
				 t = the number of years the money is invested or borrowed for
				 A = P(1+r/n)^nt
			*/

            double interestAmount;

            // Annaul percentage rate (i.e. 6%)
            double rate = 0.06;
            // Frequency of compounding (1 = yearly, 12 = monthly)
            double annualFrequency = 12.0;

            DateTime currDate = DateTime.Now;
            TimeSpan diff = currDate - startDate;

            // Time in fractional years
            double timeInYears = Math.Abs(diff.Days) / 365.0;
            double P = (double) principal;
            double A = P * Math.Pow(1.0+(rate/annualFrequency), annualFrequency*timeInYears);
            interestAmount = A - P;
            // Round to 2 decimal places
            interestAmount = Math.Round(interestAmount,2);

            //log.LogWarning($">>> timeInYears: {timeInYears}, P: {P}, A: {A}, interestAmount: {interestAmount}");

            return (decimal)interestAmount;
        }


        /*
        public DateTime parseDateTime(string inDateStr) {
            DateTime outDateTime;
            string dateStr = inDateStr.Trim();
            int spacePos = dateStr.IndexOf(' ');
            if (spacePos > -1) {
                dateStr = dateStr.Substring(0, spacePos);
            }

            if (!DateTime.TryParse(dateStr, out outDateTime)) {
                // problem with date
            }

                                //if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.None, out outDateTime))
                    // Modified to assume that the datetime in the filename format (from iPhone iOS) is a UTC datetime - this will make sure the datetime gets
                    // converted to local datetime for an accurate datetime of when the photo was taken
                    if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.AssumeUniversal, out outDateTime))
                    {
                        //log($"{fileName}, date: {dateStr}, format: {dateFormat}, DateTime: {outDateTime}");
                    }
                    else
                    {
                        Console.WriteLine($"{fileName}, date: {dateStr}, format: {dateFormat}, *** PARSE FAILED ***");
                    }

            return outDateTime;
        }
        */

                        //  "DateDue": "10/1/2006 0:00:00",
                        //     "DateDue": "10/1/2014 0:00:00",
                        //   "DateDue": "10/1/2015",
                        // "DateDue": "2024-10-01",
                        //assessmentRec.DateDue

                        // util function for a common date - take both formats, check for a space
                        //        if ($startDateTime = date_create( strtok($startDate," ") )) {


// Replace every ascii character except decimal and digits with a null, and round to 2 decimal places
/*

function strToUSD($inStr) {
	// Replace every ascii character except decimal and digits with a null
	$numericStr = preg_replace('/[\x01-\x2D\x2F\x3A-\x7F]+/', '', $inStr);
	// Convert to a float value and round down to 2 digits
	//return round(floatval($numericStr),2,PHP_ROUND_HALF_DOWN);
	return round(floatval($numericStr),2);
}

// Replace comma with null so you can use it as a CSV value
function csvFilter($inVal) {
    $inStr = (string) $inVal;
	//return preg_replace('/[\x2C]+/', '', String($inVal));
	return preg_replace('/[\x2C]+/', '', $inStr);
}

function downloadUrlToFile($url)
{
    try {
        $currTimestampStr = date("YmdHis");
        $tempFilename = sys_get_temp_dir() . DIRECTORY_SEPARATOR . $currTimestampStr . 'jjktemp.zip';
	    $tempFile = fopen($tempFilename, 'w');

        // create a new cURL resource
        $ch = curl_init();
        // set URL and other appropriate options
        curl_setopt($ch, CURLOPT_URL, $url);        // URL to call
        curl_setopt($ch, CURLOPT_FILE, $tempFile);  // Write output to this file
        curl_setopt($ch, CURLOPT_HEADER, false);
        //curl_setopt($ch, CURLOPT_USERAGENT, 'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.105 Safari/537.36');
        curl_setopt($ch, CURLOPT_POST, 0); // Don't use HTTP POST (use default of HTTP GET)
        // CURLOPT_HTTPGET is default
        curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);  // Don't check SSL
        // CURL_HTTP_VERSION_1_1
		//curl_setopt($ch, CURLOPT_RETURNTRANSFER, 1);  // return actual data for:  $file_content = curl_exec($ch);

        // grab URL and pass it to the browser
        curl_exec($ch);
		//$file_content = curl_exec($ch);

        // close cURL resource, and free up system resources
        curl_close($ch);
        fclose($tempFile);

function getMailer($mailUsername, $mailPassword, $mailServer, $mailPort) {
    //error_log(date('[Y-m-d H:i] '). "in " . basename(__FILE__,".php") . ", BEFORE " . PHP_EOL, 3, LOG_FILE);

    // Create a Transport object
    $transport = Transport::fromDsn('smtp://' . $mailUsername . ':' . $mailPassword . '@' . $mailServer . ':' . $mailPort);
    // Create a Mailer object
    $mailer = new Mailer($transport);

	return $mailer;
}

function sendMail($mailer,$toStr,$subject,$messageStr,$fromEmailAddress) {
    try {
    	$message = '<html><head><title>' . $subject .'</title></head><body>' . $messageStr . '</body></html>';

        $email = (new Email());
        $email->from($fromEmailAddress);
        $email->to($toStr);
        $email->subject($subject);
        // Set the plain-text "Body"
        //$email->text('This is the plain text body of the message.\nThanks,\nAdmin');
        // Set HTML "Body"
        $email->html($message);
        // Add an "Attachment"
        //$email->attachFromPath('/path/to/example.txt');
        // Add an "Image"
        //$email->embed(fopen('/path/to/mailor.jpg', 'r'), 'nature');

    	$mailer->send($email);
        return true;

    } catch(Exception $e) {
        error_log(date('[Y-m-d H:i:s] '). "in " . basename(__FILE__,".php") . ", sendEMail Exception = " . $e->getMessage() . PHP_EOL, 3, LOG_FILE);
        return false;
    }
}



function sendHtmlEMail($toStr,$subject,$messageStr,$fromEmailAddress) {
    try {
    	$message = '<html><head><title>' . $subject .'</title></head><body>' . $messageStr . '</body></html>';
        // Always set content-type when sending HTML email
    	$headers = "MIME-Version: 1.0" . "\r\n";
    	$headers .= "Content-type:text/html;charset=UTF-8" . "\r\n";
    	// More headers
        $headers .= 'From: ' . $fromEmailAddress . "\r\n";

        if (!function_exists('mail'))
        {
            error_log(date('[Y-m-d H:i:s] '). "in " . basename(__FILE__,".php") . ", mail() has been disabled " . PHP_EOL, 3, LOG_FILE);
            return false;
        }

    	if (mail($toStr,$subject,$message,$headers)) {
            return true;
        } else {
            return false;
        }

    } catch(Exception $e) {
        error_log(date('[Y-m-d H:i:s] '). "in " . basename(__FILE__,".php") . ", sendHtmlEMail Exception = " . $e->getMessage() . PHP_EOL, 3, LOG_FILE);
        return false;
    }
}

function truncDate($inStr) {
	$outStr = "";
	if ($inStr != null) {
		$outStr = strtok($inStr," ");
		if (strlen($outStr) > 10) {
			$outStr = substr($outStr,0,10);
		}
	}
	return $outStr;
}


function stringToMoney($inAmountStr) {
	return round(floatval( preg_replace('/[\x01-\x2D\x2F\x3A-\x7F]+/', '', $inAmountStr) ),2);
}
*/


    } // public class CommonUtil
} // namespace GrhaWeb.Function

