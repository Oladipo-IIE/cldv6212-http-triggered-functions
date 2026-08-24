using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using UserApi.Models;
using UserApi.Models.Dtos;

namespace UserApi.Functions;

public class UserRoutes
{
    private readonly ILogger<UserRoutes> _logger;

    //create a field for the service
    private readonly TableServiceClient _tableServiceClient;

    //create a parameter in constructor to hold dependency injected service
    public UserRoutes(ILogger<UserRoutes> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;

        //set your field to the received object
        _tableServiceClient = tableServiceClient;
        tableServiceClient.CreateTableIfNotExists("User");
    }

    //public name of function. Will be used in the URL if Route not specified
    [Function("CreateUser")]
    //method can be called whatever you want
    public async Task<IActionResult> CreateUser([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequest req)
    {
        var response = new ResponseBase();

        try
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            //get body of request as a string
            var body = await new StreamReader(req.Body).ReadToEndAsync();

            //convert body from json to a user object
            var user = System.Text.Json.JsonSerializer.Deserialize<User>(body);

            //alternatively, replace the previous 2 lines with
            //var user = await req.ReadFromJsonAsync<User>();

            if (user == null)
            {
                response.Success = false;
                response.Message = "Invalid user data";

                return new BadRequestObjectResult(response);
            }
            else
            {
                //get table client
                var tableClient = _tableServiceClient.GetTableClient("User");
                var result = await tableClient.GetEntityIfExistsAsync<User>("User", user.Id);

                if (result.HasValue)
                {
                    response.Success = false;
                    response.Message = "User with given Id already exists";

                    return new BadRequestObjectResult(response);
                }

                //set partitionKey and RowKey of object
                user.PartitionKey = "User";
                user.RowKey = user.Id;

                //add the new user to your table
                await tableClient.AddEntityAsync(user);

                response.Message = "User saved";
                response.Data = UserDto.ToDto(user);

                var httpResponse = new CreatedResult(user.Id, response);
                return httpResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            response.Success = false;
            response.Message = "Application Error";

            var httpResponse = new ObjectResult(response);
            httpResponse.ContentTypes.Add("application/json");
            httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
            return httpResponse;
        }
    }


    [Function("GetUser")]
    public async Task<IActionResult> GetUserAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{id}")] HttpRequest req, string id)
    {
        var response = new ResponseBase();

        try
        {
            //get user by partitionkey and rowkey
            var tableClient = _tableServiceClient.GetTableClient("User");
            var result = await tableClient.GetEntityIfExistsAsync<User>("User", id);

            if (result.HasValue)
            {
                var user = result.Value;
                response.Message = "User found";
                response.Data = UserDto.ToDto(user);

                return new OkObjectResult(response);
            }
            else
            {
                response.Message = "User Not found";
                return new NotFoundObjectResult(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            response.Success = false;
            response.Message = "Application Error";

            var httpResponse = new ObjectResult(response);
            httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
            return httpResponse;
        }
    }

    [Function("GetAllUsers")]
    public async Task<IActionResult> GetAllUsersAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequest req)
    {
        var response = new ResponseBase();

        try
        {
            //get all users
            var tableClient = _tableServiceClient.GetTableClient("User");
            var users = await tableClient.QueryAsync<User>().ToListAsync();


            //cast the objects we receive to remove all fields we do not want to expose
            var publicUserInformation = users.Select(x => UserDto.ToDto(x)).ToList();

            //create a response object with a count field, which has a count of objects in the table
            var data = new { Count = publicUserInformation.Count, Users = publicUserInformation };

            response.Message = "All users";
            response.Data = data;

            var httpResponse = new OkObjectResult(response);
            return httpResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            response.Success = false;
            response.Message = "Application Error";

            var httpResponse = new ObjectResult(response);
            httpResponse.StatusCode = StatusCodes.Status500InternalServerError;
            return httpResponse;
        }
    }

    //this method cast our user object into an anonymous object, leaving out fields that we would not like to expose
    //such as the password, partitionkey and rowkey.You could also omplement this as a class (see DTO pattern)
    //--replaced usage with UserDto
    private object GetOnlyPublicUserObjectFields(User user)
    {
        return new
        {
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Id = user.Id
        };
    }

}