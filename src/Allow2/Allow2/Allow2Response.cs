using System;
using Newtonsoft.Json;

namespace Allow2
{
    enum ResponseTypeEnum {
        Error,
        PairResult,
        CheckResult,
        Request
    }

    class Response
    {
        public ResponseTypeEnum Type { get; private set; }

        public string message { get; private set; }

        private Response(ResponseTypeEnum type)
        {
            Type = type;
        }

        protected static Response ParseFromJSON(string json)
        {
            dynamic stuff = JsonConvert.DeserializeObject(json);

            Response response = new Response(ResponseTypeEnum.Error)
            {
                message = "not implemented"
            };
            return response;
        }
    }
}
