using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using UserApi.Models;
using UserApi.Models.Dtos;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UserApi.Functions;

public class NoteRoutes
{
    private readonly ILogger<NoteRoutes> _logger;
    private readonly TableServiceClient _tableServiceClient;
    private readonly BlobServiceClient _blobServiceClient;

    private const string _noteTableName = "Note";
    private const string _attachmentTableName = "Attachment";
    private const string _blobContainerName = "attachments";

    public NoteRoutes(ILogger<NoteRoutes> logger, TableServiceClient tableServiceClient, BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
        _blobServiceClient = blobServiceClient;

        _tableServiceClient.CreateTableIfNotExists(_noteTableName);
        _tableServiceClient.CreateTableIfNotExists(_attachmentTableName);
    }

    [Function("CreateNote")]
    public async Task<IActionResult> CreateNoteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notes")] HttpRequest req)
    {
        _logger.LogInformation("Calling CreateNote.");
        var response = new ResponseBase();

        try
        {
            //use the DTO to avoid overposting attack
            var noteDto = await req.ReadFromJsonAsync<NoteDto>();

            if (noteDto == null)
            {
                response.Success = false;
                response.Message = "Note could not be parsed";

                return new BadRequestObjectResult(response);
            }

            if (string.IsNullOrWhiteSpace(noteDto.Title))
            {
                response.Success = false;
                response.Message = "Note must have a non-empty title";

                return new BadRequestObjectResult(response);
            }

            //set partitionKey and RowKey of object
            var note = new Note()
            {
                Id = noteDto.Id,
                Title = noteDto.Title,
                Content = noteDto.Content,
                PartitionKey = _noteTableName,
                RowKey = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };            

            //get table client
            var tableClient = _tableServiceClient.GetTableClient(_noteTableName);

            //add the new user to your table
            await tableClient.AddEntityAsync(note);

            response.Message = "Note saved";
            response.Data = NoteDto.ToDto(note);

            return new CreatedResult("api/notes/" + note.Id, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error creating note");

            return StatusCode500();
        }
    }

    [Function("AddAttachment")]
    public async Task<IActionResult> AddAttachmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notes/{noteId}/attachments")] HttpRequest req, string noteId)
    {
        _logger.LogInformation("Calling AddAttachment");
        var response = new ResponseBase();

        try
        {
            //check if note exists
            var notesTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var note = await notesTableClient.GetEntityIfExistsAsync<Note>(_noteTableName, noteId);

            if (!note.HasValue)
            {
                response.Success = false;
                response.Message = "Invalid note id";
                return new NotFoundObjectResult(response);
            }

            //check if req has form fields
            if (!req.HasFormContentType)
            {
                response.Success = false;
                response.Message = "Invalid content type. Expected multipart/form-data.";
                return new BadRequestObjectResult(response);
            }

            // Read the form data asynchronously
            var formCollection = await req.ReadFormAsync();

            // Extract the file by its form field name (e.g., "file")
            var file = formCollection.Files["file"];

            if (file == null || file.Length == 0)
            {
                response.Success = false;
                response.Message = "No file found in the request or file is empty.";
                return new BadRequestObjectResult(response);
            }

            // Access file properties and read data into a stream
            string fileName = file.FileName;

            // Validation
            if (file.Length > 20 * 1024 * 1024)
            {
                response.Success = false;
                response.Message = "Max 20MB allowed";
                return new BadRequestObjectResult(response);
            }

            var allowed = new[] { ".jpg", ".png", ".pdf", ".docx", ".txt", ".rtf" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowed.Contains(ext))
            {
                response.Success = false;
                response.Message = "Invalid file type: Only " + string.Join(", ", allowed.Select(n => n)) + " files allowed";
                return new BadRequestObjectResult(response);
            }

            var id = Guid.NewGuid().ToString();
            var fileUrl = await SaveFileToBlobStorageAsync(file, id + "." + ext);

            var provider = new FileExtensionContentTypeProvider();

            // Try to get the content type
            if (!provider.TryGetContentType(fileName, out string contentType))
            {
                // Fallback if the extension is unknown
                contentType = "application/octet-stream";
            }

            var attachment = new Attachment()
            {
                Id = id,
                FileName = fileName,
                Url = fileUrl,
                ContentType = contentType,
                BlobName = id + "." + ext,
                PartitionKey = noteId,
                RowKey = id
            };

            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);
            var addEntityResult = await attachmentTableClient.AddEntityAsync(attachment);

            if (!addEntityResult.IsError)
            {
                response.Message = "Attachment added";
                response.Data = AttachmentDto.ToDto(attachment);
                return new OkObjectResult(response);
            }
            else
            {
                _logger.LogError(addEntityResult.ToString());
                return StatusCode500();
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error adding attachment");

            return StatusCode500();
        }
    }

    [Function("GetAllNotes")]
    public async Task<IActionResult> GetAllNotesAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notes")] HttpRequest req)
    {
        _logger.LogInformation("Calling GetAllNotes");
        try
        {
            var noteTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);

            var notes = await noteTableClient.QueryAsync<Note>().ToListAsync();
            var notesDtos = notes.Select(x => NoteDto.ToDto(x)).ToList();

            foreach (var item in notesDtos)
            {
                var attachments = await attachmentTableClient.QueryAsync<Attachment>(x => x.PartitionKey == item.Id).ToListAsync();
                item.AttachmentCount = attachments.Count;
                item.Attachments = attachments.Select(x => AttachmentDto.ToDto(x)).ToList();
            }

            var data = new { Count = notesDtos.Count, Notes = notesDtos };

            var response = new ResponseBase();
            response.Message = "All notes";
            response.Data = data;

            var httpResponse = new OkObjectResult(response);
            return httpResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error getting all notes");

            return StatusCode500();
        }
    }

    [Function("GetNote")]
    public async Task<IActionResult> GetNoteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notes/{noteId}")] HttpRequest req, string noteId)
    {
        _logger.LogInformation("Calling GetNote");
        try
        {
            var response = new ResponseBase();
            var noteTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);

            var note = await noteTableClient.GetEntityIfExistsAsync<Note>(_noteTableName, noteId);

            if (!note.HasValue)
            {
                response.Success = false;
                response.Message = "Note does not exists";
                return new NotFoundObjectResult(response);
            }

            var noteDto = NoteDto.ToDto(note.Value);
            var attachments = await attachmentTableClient.QueryAsync<Attachment>(x => x.PartitionKey == note.Value.Id).ToListAsync();
            noteDto.AttachmentCount = attachments.Count;
            noteDto.Attachments = attachments.Select(x => AttachmentDto.ToDto(x)).ToList();

            response.Message = "Note found";
            response.Data = noteDto;
            var httpResponse = new OkObjectResult(response);
            return httpResponse;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error getting note");

            return StatusCode500();
        }
    }

    [Function("ModifyNote")]
    public async Task<IActionResult> ModifyNoteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "notes/{noteId}")] HttpRequest req, string noteId)
    {
        _logger.LogInformation("Calling ModifyNote");
        try
        {
            var response = new ResponseBase();
            var noteTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var result = await noteTableClient.GetEntityIfExistsAsync<Note>(_noteTableName, noteId);

            if (!result.HasValue)
            {
                response.Success = false;
                response.Message = "Note does not exists";
                return new NotFoundObjectResult(response);
            }
            else
            {
                var noteUpdate = await req.ReadFromJsonAsync<NoteDto>();
                if (noteUpdate == null)
                {
                    response.Success = false;
                    response.Message = "Note could not be parsed";

                    return new BadRequestObjectResult(response);
                }

                if (string.IsNullOrWhiteSpace(noteUpdate.Title))
                {
                    response.Success = false;
                    response.Message = "Note must have a non-empty title";

                    return new BadRequestObjectResult(response);
                }

                var note = result.Value;
                note.UpdatedAt = DateTime.UtcNow;
                note.Title = noteUpdate.Title;
                note.Content = noteUpdate.Content;

                var updateResult = await noteTableClient.UpdateEntityAsync<Note>(note, ETag.All, TableUpdateMode.Replace);

                if (!updateResult.IsError)
                {
                    response.Message = "Note updated";
                    response.Data = NoteDto.ToDto(note);

                    return new OkObjectResult(response);
                }
                else
                {
                    _logger.LogError($"Error occurred while updating note with Id: {note.Id}. Reason: {updateResult.ReasonPhrase}");
                    return StatusCode500();
                }

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error updating note");

            return StatusCode500();
        }
    }

    [Function("DeleteNote")]
    public async Task<IActionResult> DeleteNoteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "notes/{noteId}")] HttpRequest req, string noteId)
    {
        _logger.LogInformation("Calling DeleteNote");
        var response = new ResponseBase();

        try
        {
            //check if note exists
            var notesTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var note = await notesTableClient.GetEntityIfExistsAsync<Note>(_noteTableName, noteId);

            if (!note.HasValue)
            {
                response.Success = false;
                response.Message = "Invalid note id";
                return new NotFoundObjectResult(response);
            }

            var deleteResult = await notesTableClient.DeleteEntityAsync(note.Value);

            if (!deleteResult.IsError)
            {
                var attachmentCount = await DeleteAllAttachmentsAsync(note.Value.Id);

                response.Message = $"Note deleted; {attachmentCount} attachments deleted";
                return new OkObjectResult(response);
            }
            else
            {
                _logger.LogError($"Error occurred while deleting note with Id: {note.Value.Id}. Reason: {deleteResult.ReasonPhrase}");
                return StatusCode500();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error deleting note");

            return StatusCode500();
        }
    }

    [Function("DeleteAttachment")]
    public async Task<IActionResult> DeleteAttachmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "notes/{noteId}/attachments/{attachmentId}")] HttpRequest req, string noteId, string attachmentId)
    {
        _logger.LogInformation("Calling DeleteAttachment");
        var response = new ResponseBase();

        try
        {
            //check if note exists
            var notesTableClient = _tableServiceClient.GetTableClient(_noteTableName);
            var note = await notesTableClient.GetEntityIfExistsAsync<Note>(_noteTableName, noteId);

            if (!note.HasValue)
            {
                response.Success = false;
                response.Message = "Note not found";
                return new NotFoundObjectResult(response);
            }

            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);
            var attachment = await attachmentTableClient.GetEntityIfExistsAsync<Attachment>(note.Value.Id, attachmentId);

            if (!attachment.HasValue)
            {
                response.Success = false;
                response.Message = "Attachment not found";
                return new NotFoundObjectResult(response);
            }

            var deleteResult = await DeleteSingleAttachment(noteId, attachmentId);

            if (deleteResult)
            {
                response.Message = "attachment deleted";
                return new OkObjectResult(response);
            }
            else return StatusCode500();


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error deleting attchment");

            return StatusCode500();
        }
    }

    private async Task<int> DeleteAllAttachmentsAsync(string noteId)
    {
        try
        {
            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);
            var attachments = await attachmentTableClient.QueryAsync<Attachment>(x => x.PartitionKey == noteId).ToListAsync();

            if (attachments.Any())
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);

                foreach (var attachment in attachments)
                {
                    await containerClient.DeleteBlobIfExistsAsync(attachment.BlobName);
                    await attachmentTableClient.DeleteEntityAsync(attachment);
                }
            }

            return attachments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            throw;
        }
    }

    private async Task<bool> DeleteSingleAttachment(string noteId, string attachmentId)
    {
        try
        {
            var attachmentTableClient = _tableServiceClient.GetTableClient(_attachmentTableName);
            var attachment = await attachmentTableClient.GetEntityIfExistsAsync<Attachment>(noteId, attachmentId);

            if (attachment.HasValue)
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);

                await containerClient.DeleteBlobIfExistsAsync(attachment.Value.BlobName);
                await attachmentTableClient.DeleteEntityAsync(attachment.Value);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            throw;
        }
    }

    private async Task<string?> SaveFileToBlobStorageAsync(IFormFile file, string blobName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            await containerClient.CreateIfNotExistsAsync();
            var blobClient = containerClient.GetBlobClient(blobName);
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream);
            }

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            throw;
        }
    }

    private ObjectResult StatusCode500()
    {
        var response = new ResponseBase()
        {
            Success = false,
            Message = "Application Error"
        };

        var httpResponse = new ObjectResult(response);
        httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
        return httpResponse;
    }
}