using ARIS1.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Data
{
    public class AppDbContext : IdentityDbContext<User>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<Learner> Learners { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<LearnerSubject> LearnerSubjects { get; set; }
        public DbSet<AssessmentType> AssessmentTypes { get; set; }
        public DbSet<Assessment> Assessments { get; set; }
        public DbSet<LearnerMark> LearnerMarks { get; set; }
        public DbSet<AttendanceSession> AttendanceSessions { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // LearnerSubject composite primary key
            builder.Entity<LearnerSubject>()
                .HasKey(ls => new { ls.LearnerId, ls.SubjectId });

            // Learner relationships
            builder.Entity<Learner>()
                .HasOne(l => l.User)
                .WithOne()
                .HasForeignKey<Learner>(l => l.UserId);

            // Teacher relationships
            builder.Entity<Teacher>()
                .HasOne(t => t.User)
                .WithOne()
                .HasForeignKey<Teacher>(t => t.UserId);

            // AttendanceRecord primary key
            builder.Entity<AttendanceRecord>()
                .HasKey(a => a.RecordId);

            // AttendanceRecord status default
            builder.Entity<AttendanceRecord>()
                .Property(a => a.Status)
                .HasDefaultValue("Present");

            // Fix cascade paths - tell SQL Server not to auto-delete
            builder.Entity<AttendanceSession>()
                .HasOne(a => a.Teacher)
                .WithMany()
                .HasForeignKey(a => a.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttendanceSession>()
                .HasOne(a => a.Subject)
                .WithMany(s => s.AttendanceSessions)
                .HasForeignKey(a => a.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Assessment>()
                .HasOne(a => a.Subject)
                .WithMany(s => s.Assessments)
                .HasForeignKey(a => a.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerMark>()
                .HasOne(m => m.Assessment)
                .WithMany(a => a.LearnerMarks)
                .HasForeignKey(m => m.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerMark>()
                .HasOne(m => m.Learner)
                .WithMany(l => l.LearnerMarks)
                .HasForeignKey(m => m.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerSubject>()
                .HasOne(ls => ls.Learner)
                .WithMany(l => l.LearnerSubjects)
                .HasForeignKey(ls => ls.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerSubject>()
                .HasOne(ls => ls.Subject)
                .WithMany(s => s.LearnerSubjects)
                .HasForeignKey(ls => ls.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AssessmentType>()
                .HasOne(at => at.Subject)
                .WithMany()
                .HasForeignKey(at => at.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttendanceRecord>()
                .HasOne(ar => ar.Session)
                .WithMany(s => s.AttendanceRecords)
                .HasForeignKey(ar => ar.SessionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttendanceRecord>()
                .HasOne(ar => ar.Learner)
                .WithMany(l => l.AttendanceRecords)
                .HasForeignKey(ar => ar.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
