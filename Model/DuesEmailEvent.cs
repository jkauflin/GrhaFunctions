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
	    public decimal totalDue { get; set; }       // amount = 1234.56m;
        public string? emailAddr { get; set; }   

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
