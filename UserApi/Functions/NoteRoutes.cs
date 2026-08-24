using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using UserApi.Models;
using UserApi.Models.Dtos;

namespace UserApi.Functions;

public class NoteRoutes
{
    private readonly ILogger<NoteRoutes> _logger;
    private readonly TableServiceClient _tableServiceClient;
    private readonly BlobServiceClient _blobServiceClient;

    public NoteRoutes(ILogger<NoteRoutes> logger, TableServiceClient tableServiceClient, BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
        _blobServiceClient = blobServiceClient;

        _tableServiceClient.CreateTableIfNotExists("Note");
        _tableServiceClient.CreateTableIfNotExists("Attachment");
    }

    [Function("CreateNote")]
    public async Task<IActionResult> CreateNoteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notes")] HttpRequest req)
    {
        _logger.LogInformation("Calling CreateNote.");
        var response = new ResponseBase();

        try
        {
            var note = await req.ReadFromJsonAsync<Note>();

            if (note == null)
            {
                response.Success = false;
                response.Message = "Note could not be parsed";

                return new BadRequestObjectResult(response);
            }
            else
            {
                if (note.Title == null || note.Title.Length == 0)
                {
                    response.Success = false;
                    response.Message = "Note must have a non-empty title";

                    return new BadRequestObjectResult(response);
                }

                //set partitionKey and RowKey of object
                note.PartitionKey = "Note";
                note.RowKey = Guid.NewGuid().ToString();
                note.CreatedAt = DateTime.Now;
                note.UpdatedAt = DateTime.Now;

                //get table client
                var tableClient = _tableServiceClient.GetTableClient("Note");

                //add the new user to your table
                await tableClient.AddEntityAsync(note);

                response.Message = "Note saved";
                response.Data = NoteDto.ToDto(note);

                var httpResponse = new CreatedResult(note.Id, response);
                return httpResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            return StatusCode500();
        }
    }

    [Function("AddAttachment")]
    public async Task<IActionResult> AddAttachmentAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notes/{notesid}/attachments")] HttpRequest req, string notesid)
    {
        _logger.LogInformation("Calling AddAttachment");
        var response = new ResponseBase();

        try
        {
            if (!req.HasFormContentType)
            {
                response.Success = false;
                response.Message = "Invalid content type. Expected multipart/form-data.";
                return new BadRequestObjectResult(response);
            }

            // 2. Read the form data asynchronously
            var formCollection = await req.ReadFormAsync();

            // 3. Extract the file by its form field name (e.g., "file")
            var file = formCollection.Files["file"];

            if (file == null || file.Length == 0)
            {
                response.Success = false;
                response.Message = "No file found in the request or file is empty.";
                return new BadRequestObjectResult(response);
            }

            // 4. Access file properties and read data into a stream
            string fileName = file.FileName;

            // Validation
            if (file.Length > 20 * 1024 * 1024)
            {
                response.Success = false;
                response.Message = "Max 20MB allowed";
                return new BadRequestObjectResult(response);
            }

            var allowed = new[] { ".jpg", ".png", ".pdf", ".docx", ".txt", ".rtf" };
            var ext = Path.GetExtension(file.FileName).ToLower();

            if (!allowed.Contains(ext))
            {
                response.Success = false;
                response.Message = "Invalid file type: Only " + string.Join(", ", allowed.Select(n => n)) + " files allowed";
                return new BadRequestObjectResult(response);
            }

            var fileUrl = await SaveFileToBlobStorageAsync(file);

            var provider = new FileExtensionContentTypeProvider();

            // Try to get the content type
            if (!provider.TryGetContentType(fileName, out string contentType))
            {
                // Fallback if the extension is unknown
                contentType = "application/octet-stream";
            }

            var id = Guid.NewGuid().ToString();

            var attachment = new Attachment()
            {
                Id = id,
                FileName = fileName,
                Url = fileUrl,
                ContentType = contentType,
                PartitionKey = notesid,
                RowKey = id
            };

            var attachmentTableClient = _tableServiceClient.GetTableClient("Attachment");
            var table_response = await attachmentTableClient.AddEntityAsync(attachment);

            if (!table_response.IsError)
            {
                response.Message = "Attachment added";
                response.Data = AttachmentDto.ToDto(attachment);
                return new CreatedResult(id, response);
            }
            else
            {
                _logger.LogError(table_response.ToString());
                return StatusCode500();
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            return StatusCode500();
        }
    }

    private async Task<string?> SaveFileToBlobStorageAsync(IFormFile file)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("attachments");
            containerClient.CreateIfNotExists();
            var blobClient = containerClient.GetBlobClient(Guid.NewGuid().ToString()
                + System.IO.Path.GetExtension(file.FileName));
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream);
            }

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
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