using System;
using System.Collections.Generic;
using System.Text;

namespace UserApi.Models.Dtos
{
    internal class NoteDto
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int AttachmentCount { get; set; } = 0;

        public IEnumerable<AttachmentDto> Attachments { get; set; } = Enumerable.Empty<AttachmentDto>();

        public static NoteDto? ToDto(Note note)
        {
            if (note == null)
                return null;

            return new NoteDto
            {
                Id = note.Id,
                Title = note.Title,
                Content = note.Content,
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt
            };
        }
    }
}
