using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("AddAttachment")]
    public IActionResult AddAttachment([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route ="notes/{notesid}/attachments")] HttpRequest req, string notesid)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}