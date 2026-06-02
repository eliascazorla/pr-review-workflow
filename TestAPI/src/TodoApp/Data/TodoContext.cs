// Copyright (c) Martin Costello, 2021. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Microsoft.EntityFrameworkCore;

namespace TodoApp.Data;

public class TodoContext(DbContextOptions<TodoContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Items { get; set; } = default!;
    public DbSet<Attachment> Attachments { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Attachment>()
            .HasOne(a => a.Todo)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.TodoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
