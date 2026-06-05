namespace TodoApp.Models;

public class AttachmentModel
{
    public string Id { get; set; } = default!;
    public string TodoId { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string FileUrl { get; set; } = default!;
    public string FileType { get; set; } = default!;
    public long FileSize { get; set; }
    public string UploadedAt { get; set; } = default!;
}

public class CreateAttachmentModel
{
    public string FileName { get; set; } = default!;
    public string FileUrl { get; set; } = default!;
    public string FileType { get; set; } = default!;
    public long FileSize { get; set; }
}

public class CreatedAttachmentModel
{
    public string Id { get; set; } = default!;
}
