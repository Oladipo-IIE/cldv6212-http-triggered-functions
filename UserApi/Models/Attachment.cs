using Azure;
using Azure.Data.Tables;

namespace UserApi.Models
{
    internal class Attachment : ITableEntity
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public string Url { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
