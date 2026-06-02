using Microsoft.EntityFrameworkCore;

namespace TodoApp.Data;

public class AttachmentRepository : IAttachmentRepository
{
    private TimeProvider tp;
    private TodoContext ctx;
    
    public AttachmentRepository(TimeProvider timeProvider, TodoContext context)
    {
        tp = timeProvider;
        ctx = context;
    }

    public async Task<Attachment> AddAttachmentAsync(
        string userId, 
        Guid todoId, 
        string fileName, 
        string fileUrl, 
        string fileType, 
        long fileSize, 
        CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        
        var todo = await ctx.Items.FirstOrDefaultAsync(x => x.Id == todoId && x.UserId == userId, ct);
        
        if (todo == null)
        {
            throw new Exception("Todo not found");
        }

        var att = new Attachment
        {
            Id = Guid.NewGuid(),
            TodoId = todoId,
            FileName = fileName,
            FileUrl = fileUrl,
            FileType = fileType,
            FileSize = fileSize,
            UploadedAt = tp.GetUtcNow().UtcDateTime
        };

        ctx.Add(att);
        await ctx.SaveChangesAsync(ct);
        return att;
    }

    public async Task<bool> DeleteAttachmentAsync(string userId, Guid attachmentId, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        
        var a = await ctx.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        
        if (a == null) return false;

        var todo = await ctx.Items.FirstOrDefaultAsync(x => x.Id == a.TodoId && x.UserId == userId, ct);
        
        if (todo == null) return false;

        ctx.Attachments.Remove(a);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        
        return await ctx.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
    }

    public async Task<List<Attachment>> GetAttachmentsByTodoAsync(string userId, Guid todoId, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        
        var todo = await ctx.Items
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == todoId && x.UserId == userId, ct);

        return todo?.Attachments.ToList() ?? new List<Attachment>();
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
        => await ctx.Database.EnsureCreatedAsync(ct);
}
