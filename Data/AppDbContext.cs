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

        // New School entity
        public DbSet<School> Schools { get; set; }

        public DbSet<Learner> Learners { get; set; }
        public DbSet<SchoolClass> SchoolClasses { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Parent> Parents { get; set; }
        public DbSet<ParentLearner> ParentLearners { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<LearnerSubject> LearnerSubjects { get; set; }
        public DbSet<AssessmentType> AssessmentTypes { get; set; }
        public DbSet<Assessment> Assessments { get; set; }
        public DbSet<LearnerMark> LearnerMarks { get; set; }
        public DbSet<AttendanceSession> AttendanceSessions { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        public DbSet<AssessmentQuestion> AssessmentQuestions { get; set; }
        public DbSet<LearnerQuestionMark> LearnerQuestionMarks { get; set; }
        public DbSet<Intervention> Interventions { get; set; }
        public DbSet<WeightingStructure> WeightingStructures { get; set; }
        public DbSet<WeightingNode> WeightingNodes { get; set; }
        public DbSet<GradeBand> GradeBands { get; set; }
        public DbSet<WeightingValidation> WeightingValidations { get; set; }
        public DbSet<LearnerYearRecord> LearnerYearRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ===== SCHOOL CONFIGURATION =====
            builder.Entity<School>()
                .HasKey(s => s.SchoolId);

            builder.Entity<School>()
                .HasIndex(s => s.Code)
                .IsUnique();

            // School -> Users (one-to-many)
            builder.Entity<User>()
                .HasOne(u => u.School)
                .WithMany(s => s.Users)
                .HasForeignKey(u => u.SchoolId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false); // SuperAdmin has no school

            // School -> Subjects (one-to-many)
            builder.Entity<Subject>()
                .HasOne(s => s.School)
                .WithMany(sch => sch.Subjects)
                .HasForeignKey(s => s.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // ===== EXISTING CONFIGURATIONS =====

            // LearnerSubject composite primary key
            builder.Entity<LearnerSubject>()
                .HasKey(ls => new { ls.LearnerId, ls.SubjectId });

            // Learner relationships
            builder.Entity<Learner>()
                .HasOne(l => l.User)
                .WithOne()
                .HasForeignKey<Learner>(l => l.UserId);

            // SchoolClass relationships
            builder.Entity<SchoolClass>()
                .HasKey(c => c.ClassId);

            builder.Entity<SchoolClass>()
                .HasOne(c => c.School)
                .WithMany()
                .HasForeignKey(c => c.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SchoolClass>()
                .HasIndex(c => new { c.SchoolId, c.Grade, c.Name })
                .IsUnique();

            builder.Entity<Learner>()
                .HasOne(l => l.Class)
                .WithMany(c => c.Learners)
                .HasForeignKey(l => l.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // Teacher relationships
            builder.Entity<Teacher>()
                .HasOne(t => t.User)
                .WithOne()
                .HasForeignKey<Teacher>(t => t.UserId);

            // Parent relationships (mirrors Teacher's 1:1 User link)
            builder.Entity<Parent>()
                .HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<Parent>(p => p.UserId);

            // ParentLearner composite primary key (mirrors LearnerSubject)
            builder.Entity<ParentLearner>()
                .HasKey(pl => new { pl.ParentId, pl.LearnerId });

            builder.Entity<ParentLearner>()
                .HasOne(pl => pl.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(pl => pl.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ParentLearner>()
                .HasOne(pl => pl.Learner)
                .WithMany(l => l.Guardians)
                .HasForeignKey(pl => pl.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // AttendanceRecord primary key
            builder.Entity<AttendanceRecord>()
                .HasKey(a => a.RecordId);

            // AttendanceRecord status default
            builder.Entity<AttendanceRecord>()
                .Property(a => a.Status)
                .HasDefaultValue("Present");

            // Learner status default — existing/new learners default to Active
            builder.Entity<Learner>()
                .Property(l => l.Status)
                .HasDefaultValue("Active");

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

            // AssessmentQuestion relationship
            builder.Entity<AssessmentQuestion>()
                .HasOne(aq => aq.Assessment)
                .WithMany()
                .HasForeignKey(aq => aq.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // LearnerQuestionMark relationship
            builder.Entity<LearnerQuestionMark>()
                .HasOne(lqm => lqm.Question)
                .WithMany(q => q.LearnerMarks)
                .HasForeignKey(lqm => lqm.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<LearnerQuestionMark>()
                .HasOne(lqm => lqm.Learner)
                .WithMany()
                .HasForeignKey(lqm => lqm.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Intervention relationship
            builder.Entity<Intervention>()
                .HasOne(i => i.Learner)
                .WithMany()
                .HasForeignKey(i => i.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Intervention>()
                .HasOne(i => i.Question)
                .WithMany()
                .HasForeignKey(i => i.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            // WeightingStructure relationships
            builder.Entity<WeightingStructure>()
                .HasOne(ws => ws.Subject)
                .WithMany()
                .HasForeignKey(ws => ws.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // WeightingNode relationships - self-referencing hierarchy
            builder.Entity<WeightingNode>()
                .HasOne(wn => wn.WeightingStructure)
                .WithMany(ws => ws.RootNodes)
                .HasForeignKey(wn => wn.WeightingStructureId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WeightingNode>()
                .HasOne(wn => wn.ParentNode)
                .WithMany(wn => wn.ChildNodes)
                .HasForeignKey(wn => wn.ParentNodeId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<WeightingNode>()
                .HasOne(wn => wn.AssessmentType)
                .WithMany()
                .HasForeignKey(wn => wn.AssessmentTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // GradeBand relationships
            builder.Entity<GradeBand>()
                .HasOne(gb => gb.Subject)
                .WithMany()
                .HasForeignKey(gb => gb.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Index for common queries
            builder.Entity<WeightingStructure>()
                .HasIndex(ws => new { ws.SubjectId, ws.Term })
                .IsUnique();

            builder.Entity<GradeBand>()
                .HasIndex(gb => new { gb.SubjectId, gb.MinPercentage, gb.MaxPercentage });

            // ===== DECIMAL COLUMN PRECISION =====
            builder.Entity<Assessment>()
                .Property(a => a.MaxMark)
                .HasColumnType("decimal(10,4)");

            builder.Entity<AssessmentQuestion>()
                .Property(aq => aq.MaxMark)
                .HasColumnType("decimal(10,4)");

            builder.Entity<AssessmentType>()
                .Property(at => at.WeightPercentage)
                .HasColumnType("decimal(10,4)");

            builder.Entity<LearnerMark>()
                .Property(m => m.MarksAwarded)
                .HasColumnType("decimal(10,4)");

            builder.Entity<LearnerQuestionMark>()
                .Property(lqm => lqm.MarksAwarded)
                .HasColumnType("decimal(10,4)");

            builder.Entity<WeightingNode>()
                .Property(wn => wn.Weighting)
                .HasColumnType("decimal(10,4)");

            builder.Entity<GradeBand>()
                .Property(gb => gb.MinPercentage)
                .HasColumnType("decimal(10,4)");

            builder.Entity<GradeBand>()
                .Property(gb => gb.MaxPercentage)
                .HasColumnType("decimal(10,4)");

            builder.Entity<Intervention>()
                .Property(i => i.PercentageScore)
                .HasColumnType("decimal(10,4)");

            // Weighting Validation relationships
            builder.Entity<WeightingValidation>()
                .HasOne(wv => wv.WeightingStructure)
                .WithMany(ws => ws.Validations)
                .HasForeignKey(wv => wv.WeightingStructureId)
                .OnDelete(DeleteBehavior.Cascade);

            // LearnerYearRecord relationships — append-only per-learner-per-year archive trail
            builder.Entity<LearnerYearRecord>()
                .HasOne(r => r.Learner)
                .WithMany(l => l.YearRecords)
                .HasForeignKey(r => r.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerYearRecord>()
                .HasOne(r => r.Class)
                .WithMany()
                .HasForeignKey(r => r.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LearnerYearRecord>()
                .HasIndex(r => new { r.LearnerId, r.AcademicYear })
                .IsUnique();
        }
    }
}