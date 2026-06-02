using TodoApp.Models;

namespace TodoApp.Services;

public interface IAttachmentService
{
    Task<string> AddAttachmentAsync(string userId, Guid todoId, string fileName, string fileUrl, string fileType, long fileSize, CancellationToken ct);
    Task<bool> RemoveAttachmentAsync(string userId, Guid attachmentId, CancellationToken ct);
    Task<List<AttachmentModel>> GetAttachmentsByTodoAsync(string userId, Guid todoId, CancellationToken ct);
}
