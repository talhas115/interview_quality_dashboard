using Microsoft.EntityFrameworkCore;

namespace InterviewAudit.Infrastructure.Persistence
{
    public class InterviewAuditDbContext : DbContext
    {
        public InterviewAuditDbContext(DbContextOptions<InterviewAuditDbContext> options) : base(options)
        {
        }

        public DbSet<ProcessedMeetingEntity> ProcessedMeetings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProcessedMeetingEntity>(entity =>
            {
                entity.ToTable("ProcessedMeetings");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.GroupId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.MeetingId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.CandidateId).HasMaxLength(100);
                entity.Property(e => e.CandidateName).HasMaxLength(100);
                entity.Property(e => e.InterviewerName).HasMaxLength(100);
                entity.Property(e => e.StartDateTime).HasMaxLength(100);
                entity.Property(e => e.EndDateTime).HasMaxLength(100);
                
                // Add unique constraint for GroupId + MeetingId
                entity.HasIndex(e => new { e.GroupId, e.MeetingId }).IsUnique();
            });
        }
    }
}
