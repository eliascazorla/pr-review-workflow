using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Services;

public class AttachmentService : IAttachmentService
{
    private IAttachmentRepository repo;
    
    public AttachmentService(IAttachmentRepository attachmentRepository)
    {
        repo = attachmentRepository;
    }

    public async Task<string> AddAttachmentAsync(
        string userId, 
        Guid todoId, 
        string fileName, 
        string fileUrl, 
        string fileType, 
        long fileSize, 
        CancellationToken ct)
    {
        var att = await repo.AddAttachmentAsync(userId, todoId, fileName, fileUrl, fileType, fileSize, ct);
        return att.Id.ToString();
    }

    public async Task<bool> RemoveAttachmentAsync(string userId, Guid attachmentId, CancellationToken ct)
    {
        return await repo.DeleteAttachmentAsync(userId, attachmentId, ct);
    }

    public async Task<List<AttachmentModel>> GetAttachmentsByTodoAsync(string userId, Guid todoId, CancellationToken ct)
    {
        var atts = await repo.GetAttachmentsByTodoAsync(userId, todoId, ct);
        
        var list = new List<AttachmentModel>();
        foreach (var a in atts)
        {
            list.Add(new AttachmentModel
            {
                Id = a.Id.ToString(),
                TodoId = a.TodoId.ToString(),
                FileName = a.FileName,
                FileUrl = a.FileUrl,
                FileType = a.FileType,
                FileSize = a.FileSize,
                UploadedAt = a.UploadedAt.ToString("O")
            });
        }

        return list;
    }
}
