using Azure;
using Azure.Data.Tables;

namespace UserApi.Models
{
    internal class Note : ITableEntity
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
