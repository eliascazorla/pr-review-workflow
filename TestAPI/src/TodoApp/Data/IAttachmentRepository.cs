namespace TodoApp.Data;

public interface IAttachmentRepository
{
    Task<Attachment> AddAttachmentAsync(string userId, Guid todoId, string fileName, string fileUrl, string fileType, long fileSize, CancellationToken ct = default);
    Task<bool> DeleteAttachmentAsync(string userId, Guid attachmentId, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken ct = default);
    Task<List<Attachment>> GetAttachmentsByTodoAsync(string userId, Guid todoId, CancellationToken ct = default);
}
