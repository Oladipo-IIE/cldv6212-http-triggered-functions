namespace UserApi.Models
{
    internal class ResponseBase
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; }

        public object Data { get; set; }
    }
}