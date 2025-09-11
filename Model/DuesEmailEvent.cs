using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace GrhaWeb.Function.Model
{
    public class DuesEmailEvent
    {
        public string? id { get; set; }   
        public string? parcelId { get; set; }   
        public string? emailAddr { get; set; }   
        /*
        public string? mailingName { get; set; }   
        public string? parcelLocation { get; set; }   
        public string? ownerPhone { get; set; }   
        public string? ownerEmail1 { get; set; }   
        public string? ownerEmail2 { get; set; }   
        public int fy { get; set; }   
        public string? DuesAmt { get; set; }
        public int Paid { get; set; }
	    public decimal totalDue { get; set; }       // amount = 1234.56m;
        */
        public string? hoaName { get; set; }   
        public string? hoaNameShort { get; set; }   
        public string? hoaAddress1 { get; set; }   
        public string? hoaAddress2 { get; set; }   
        public string? helpNotes { get; set; }
        public string? duesUrl { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
