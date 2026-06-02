using Microsoft.AspNetCore.Http.HttpResults;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp;

public static class AttachmentEndpoints
{
    public static IServiceCollection AddAttachmentServices(this IServiceCollection services)
    {
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapAttachmentRoutes(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/attachments")
                           .RequireAuthorization();
        
        group.MapPost("/", async Task<Results<Created<CreatedAttachmentModel>, ProblemHttpResult>> (
            Guid todoId,
            CreateAttachmentModel model,
            [AsParameters] AttachmentRequestContext context,
            CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrEmpty(model.FileName) || string.IsNullOrEmpty(model.FileUrl))
                {
                    return TypedResults.Problem("Invalid file data.", statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var id = await context.Service.AddAttachmentAsync(
                        context.User, 
                        todoId, 
                        model.FileName, 
                        model.FileUrl, 
                        model.FileType, 
                        model.FileSize, 
                        cancellationToken);

                    return TypedResults.Created($"/api/attachments/{id}", new CreatedAttachmentModel { Id = id });
                }
                catch
                {
                    return TypedResults.Problem("Failed to add attachment.", statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Add an attachment to a Todo")
            .WithDescription("Adds a new attachment to the specified todo item.");

        group.MapDelete("/{id}", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id,
            [AsParameters] AttachmentRequestContext context,
            CancellationToken cancellationToken) =>
            {
                var wasDeleted = await context.Service.RemoveAttachmentAsync(context.User, id, cancellationToken);
                return wasDeleted switch
                {
                    true => TypedResults.NoContent(),
                    false => TypedResults.Problem("Attachment not found.", statusCode: StatusCodes.Status404NotFound),
                };
            })
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Delete an attachment")
            .WithDescription("Deletes the attachment with the specified ID.");

        group.MapGet("/todo/{todoId}", async Task<Results<Ok<TodoWithAttachmentsModel>, ProblemHttpResult>> (
            Guid todoId,
            [AsParameters] AttachmentRequestContext context,
            CancellationToken cancellationToken) =>
            {
                try
                {
                    var atts = await context.Service.GetAttachmentsByTodoAsync(context.User, todoId, cancellationToken);
                    
                    var model = new TodoWithAttachmentsModel
                    {
                        TodoId = todoId.ToString(),
                        Attachments = atts
                    };

                    return TypedResults.Ok(model);
                }
                catch
                {
                    return TypedResults.Problem("Todo not found.", statusCode: StatusCodes.Status404NotFound);
                }
            })
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Get all attachments for a Todo")
            .WithDescription("Gets all attachments for the specified todo item.");

        return builder;
    }

    private record struct AttachmentRequestContext(string User, IAttachmentService Service)
    {
        public static ValueTask<AttachmentRequestContext> BindAsync(HttpContext context)
        {
            var userId = context.User.GetUserId();
            var service = context.RequestServices.GetRequiredService<IAttachmentService>();
            return ValueTask.FromResult(new AttachmentRequestContext(userId, service));
        }
    }
}

public class TodoWithAttachmentsModel
{
    public string TodoId { get; set; } = default!;
    public List<AttachmentModel> Attachments { get; set; } = new();
}
