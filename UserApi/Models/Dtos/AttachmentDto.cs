using System;
using System.Collections.Generic;
using System.Text;

namespace UserApi.Models.Dtos
{
    internal class AttachmentDto
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public string Url { get; set; }

        public static AttachmentDto? ToDto(Attachment attachment)
        {
            if (attachment == null)
                return null;

            return new AttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                Url = attachment.Url
            };
        }
    }
}
