using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserApi.Models
{
    internal class ResponseBase
    {
        public bool Success {  get; set; } = true;
        public string Message { get; set; }

        public object Data { get; set; }
    }
}
