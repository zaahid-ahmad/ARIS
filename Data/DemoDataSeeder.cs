using ARIS1.Models;
using ARIS1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Data
{
    // One-off demo-data generator for exploratory/manual testing against a realistic
    // school-sized dataset. Not run automatically on every startup — triggered only via
    // `dotnet run -- --seed-demo` (see Program.cs). Idempotent: skips entirely if the
    // Riverside school already exists, so re-running the flag is always safe.
    public static class DemoDataSeeder
    {
        private const string SchoolCode = "RIVERSIDE";
        private const string SchoolDomain = "riverside.school.co.za";
        private const int AcademicYear = 2026;

        private static readonly string[] FirstNames =
        {
            "Thabo","Sipho","Lerato","Naledi","Kagiso","Boitumelo","Karabo","Tshepo","Nkosana","Zanele",
            "Andile","Nomvula","Lindiwe","Bongani","Mpho","Refilwe","Katlego","Thandiwe","Sibusiso","Ayanda",
            "Johan","Pieter","Anel","Marius","Elmarie","Willem","Suzanne","Hendrik","Amore","Riaan",
            "James","Emily","Michael","Sarah","David","Grace","Daniel","Chloe","Ryan","Megan",
            "Farhana","Yusuf","Aisha","Imraan","Zainab","Rashid"
        };

        private static readonly string[] LastNames =
        {
            "Nkosi","Dlamini","Khumalo","Mokoena","Molefe","Zulu","Ndlovu","Mahlangu","Sithole","Mabaso",
            "Van der Merwe","Botha","Pretorius","Van Wyk","Nel","Fourie","Kruger","Coetzee","Steyn","Venter",
            "Smith","Jones","Brown","Wilson","Taylor","Anderson","Clarke","Robinson",
            "Naidoo","Pillay","Govender","Reddy","Moodley",
            "Khan","Patel","Adams"
        };

        private static readonly string[] PersonalEmailDomains = { "gmail.com", "outlook.com", "webmail.co.za", "yahoo.com" };

        // (Name, IsCompulsory) — index also drives which of the 10 teachers owns this
        // subject across all 3 grades (teacher i teaches SubjectCatalog[i] for every grade).
        private static readonly (string Name, bool Compulsory)[] SubjectCatalog =
        {
            ("Mathematics", true),
            ("English Home Language", true),
            ("Afrikaans First Additional Language", true),
            ("Life Orientation", true),
            ("Physical Science", false),
            ("Life Sciences", false),
            ("Accounting", false),
            ("Geography", false),
            ("History", false),
            ("Business Studies", false),
        };

        private static readonly int[] Grades = { 10, 11, 12 };
        private static readonly string[] ClassNames = { "A", "B", "C" };
        private const int LearnersPerClass = 28;

        private static readonly (string Name, decimal Weight, decimal MaxMark)[] TypeWeights =
        {
            ("Assignment", 20m, 20m),
            ("Test", 30m, 50m),
            ("Exam", 50m, 100m),
        };

        private record TermWindow(int Term, DateTime Start, DateTime End);

        // SA school calendar; all three windows fall before "today" (2026-09-01) so
        // nothing looks dated in the future.
        private static readonly TermWindow[] Terms =
        {
            new(1, new DateTime(2026, 1, 14), new DateTime(2026, 3, 13)),
            new(2, new DateTime(2026, 4, 8), new DateTime(2026, 6, 12)),
            new(3, new DateTime(2026, 7, 15), new DateTime(2026, 8, 28)),
        };

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var weightingService = serviceProvider.GetRequiredService<WeightingService>();
            var interventionService = serviceProvider.GetRequiredService<InterventionService>();

            if (await context.Schools.AnyAsync(s => s.Code == SchoolCode))
            {
                Console.WriteLine("[DemoDataSeeder] Riverside Secondary School already exists — skipping.");
                return;
            }

            var rng = new Random(20260901);
            var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Console.WriteLine("[DemoDataSeeder] Seeding Riverside Secondary School — this can take several minutes...");

            // ---- School ----
            var school = new School
            {
                Name = "Riverside Secondary School",
                Code = SchoolCode,
                Address = "45 Bridge Road, Riverside",
                Email = $"info@{SchoolDomain}",
                Phone = "011-555-0142",
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };
            context.Schools.Add(school);
            await context.SaveChangesAsync();

            // ---- Admin ----
            var adminEmail = $"admin@{SchoolDomain}";
            usedEmails.Add(adminEmail);
            var adminUser = new User
            {
                UserName = adminEmail,
                Email = adminEmail,
                Fullname = "Precious Mahlangu",
                IsActive = true,
                EmailConfirmed = true,
                SchoolId = school.SchoolId
            };
            await userManager.CreateAsync(adminUser, "Admin@1234");
            await userManager.AddToRoleAsync(adminUser, "Admin");

            // ---- Classes ----
            var classesByGradeAndName = new Dictionary<(int Grade, string Name), SchoolClass>();
            foreach (var grade in Grades)
            {
                foreach (var className in ClassNames)
                {
                    var cls = new SchoolClass { SchoolId = school.SchoolId, Grade = grade, Name = className };
                    context.SchoolClasses.Add(cls);
                    classesByGradeAndName[(grade, className)] = cls;
                }
            }
            await context.SaveChangesAsync();

            // ---- Teachers: one per SubjectCatalog entry, teaching it across all 3 grades ----
            var teacherIdBySubjectIndex = new int[SubjectCatalog.Length];
            var teacherSample = new List<(string Email, string Fullname)>();
            for (int i = 0; i < SubjectCatalog.Length; i++)
            {
                var (first, last) = RandomName(rng);
                var email = MakeUniqueEmail(first, last, SchoolDomain, usedEmails);
                var user = new User
                {
                    UserName = email,
                    Email = email,
                    Fullname = $"{first} {last}",
                    IsActive = true,
                    EmailConfirmed = true,
                    SchoolId = school.SchoolId
                };
                await userManager.CreateAsync(user, "Teacher@1234");
                await userManager.AddToRoleAsync(user, "Teacher");

                var teacher = new Teacher { UserId = user.Id };
                context.Teachers.Add(teacher);
                await context.SaveChangesAsync();

                teacherIdBySubjectIndex[i] = teacher.TeacherId;
                if (teacherSample.Count < 3) teacherSample.Add((email, user.Fullname));
            }
            Console.WriteLine($"[DemoDataSeeder] {SubjectCatalog.Length} teachers created.");

            // ---- Subjects ----
            var subjectsByGrade = new Dictionary<int, List<Subject>>();
            var compulsorySubjectsByGrade = new Dictionary<int, List<Subject>>();
            var electiveSubjectsByGrade = new Dictionary<int, List<Subject>>();
            foreach (var grade in Grades)
            {
                subjectsByGrade[grade] = new List<Subject>();
                compulsorySubjectsByGrade[grade] = new List<Subject>();
                electiveSubjectsByGrade[grade] = new List<Subject>();

                for (int i = 0; i < SubjectCatalog.Length; i++)
                {
                    var (name, compulsory) = SubjectCatalog[i];
                    var subject = new Subject
                    {
                        Name = name,
                        Grade = grade,
                        TeacherId = teacherIdBySubjectIndex[i],
                        AcademicYear = AcademicYear,
                        SchoolId = school.SchoolId
                    };
                    context.Subjects.Add(subject);
                    subjectsByGrade[grade].Add(subject);
                    (compulsory ? compulsorySubjectsByGrade : electiveSubjectsByGrade)[grade].Add(subject);
                }
            }
            await context.SaveChangesAsync();

            var allSubjects = subjectsByGrade.Values.SelectMany(s => s).ToList();
            var learnersBySubject = allSubjects.ToDictionary(s => s.SubjectId, _ => new List<int>());
            Console.WriteLine($"[DemoDataSeeder] {allSubjects.Count} subjects created.");

            // ---- Learners + enrollments ----
            var learnerTraits = new Dictionary<int, (double Ability, double Reliability)>();
            var learnerUserInfo = new Dictionary<int, (string Email, string Fullname)>();
            var learnerSample = new List<(string Email, string Fullname, string RiskHint)>();

            foreach (var grade in Grades)
            {
                foreach (var className in ClassNames)
                {
                    var cls = classesByGradeAndName[(grade, className)];
                    var classLearners = new List<(User User, Learner Learner)>();

                    for (int i = 0; i < LearnersPerClass; i++)
                    {
                        var (first, last) = RandomName(rng);
                        var email = MakeUniqueEmail(first, last, "learner." + SchoolDomain, usedEmails);
                        var user = new User
                        {
                            UserName = email,
                            Email = email,
                            Fullname = $"{first} {last}",
                            IsActive = true,
                            EmailConfirmed = true,
                            SchoolId = school.SchoolId
                        };
                        await userManager.CreateAsync(user, "Learner@1234");
                        await userManager.AddToRoleAsync(user, "Learner");

                        var learner = new Learner
                        {
                            UserId = user.Id,
                            Grade = grade,
                            ClassId = cls.ClassId,
                            EnrollmentYear = AcademicYear - (grade - 10)
                        };
                        context.Learners.Add(learner);
                        classLearners.Add((user, learner));
                    }
                    await context.SaveChangesAsync();

                    foreach (var (user, learner) in classLearners)
                    {
                        var ability = SampleAbility(rng);
                        var reliability = Math.Clamp(ability + (rng.NextDouble() - 0.5) * 0.3, 0.05, 0.99);
                        learnerTraits[learner.LearnerId] = (ability, reliability);
                        learnerUserInfo[learner.LearnerId] = (user.Email!, user.Fullname);

                        foreach (var subject in compulsorySubjectsByGrade[grade])
                        {
                            context.LearnerSubjects.Add(new LearnerSubject { LearnerId = learner.LearnerId, SubjectId = subject.SubjectId, AcademicYear = AcademicYear });
                            learnersBySubject[subject.SubjectId].Add(learner.LearnerId);
                        }
                        foreach (var subject in PickRandom(electiveSubjectsByGrade[grade], 3, rng))
                        {
                            context.LearnerSubjects.Add(new LearnerSubject { LearnerId = learner.LearnerId, SubjectId = subject.SubjectId, AcademicYear = AcademicYear });
                            learnersBySubject[subject.SubjectId].Add(learner.LearnerId);
                        }

                        if (learnerSample.Count < 6)
                        {
                            var hint = ability < 0.4 ? "likely Critical/High risk" : ability > 0.75 ? "likely Low risk" : "likely Moderate risk";
                            learnerSample.Add((user.Email!, user.Fullname, hint));
                        }
                    }
                    await context.SaveChangesAsync();
                }
                Console.WriteLine($"[DemoDataSeeder] Grade {grade} learners + enrollments created.");
            }

            // ---- Parents (~90% of learners get one linked parent) ----
            int parentCount = 0;
            foreach (var learnerId in learnerTraits.Keys)
            {
                if (rng.NextDouble() >= 0.9) continue;

                var (_, learnerFullname) = learnerUserInfo[learnerId];
                var learnerLastName = learnerFullname.Split(' ').Last();
                var (parentFirst, _) = RandomName(rng);
                var domain = PersonalEmailDomains[rng.Next(PersonalEmailDomains.Length)];
                var email = MakeUniqueEmail(parentFirst, learnerLastName, domain, usedEmails);

                var parentUser = new User
                {
                    UserName = email,
                    Email = email,
                    Fullname = $"{parentFirst} {learnerLastName}",
                    IsActive = true,
                    EmailConfirmed = true,
                    SchoolId = school.SchoolId
                };
                await userManager.CreateAsync(parentUser, "Parent@1234");
                await userManager.AddToRoleAsync(parentUser, "Parent");

                var parent = new Parent { UserId = parentUser.Id };
                parent.Children.Add(new ParentLearner
                {
                    LearnerId = learnerId,
                    Relationship = rng.NextDouble() < 0.5 ? "Mother" : "Father",
                    CreatedDate = DateTime.UtcNow
                });
                context.Parents.Add(parent);
                parentCount++;

                if (parentCount % 50 == 0)
                {
                    await context.SaveChangesAsync();
                    Console.WriteLine($"[DemoDataSeeder] {parentCount} parents created so far...");
                }
            }
            await context.SaveChangesAsync();
            Console.WriteLine($"[DemoDataSeeder] {parentCount} parents created.");

            // ---- Assessment structure: types, weighting, assessments, Term 3 exam questions ----
            var assessmentsBySubjectTerm = new Dictionary<int, Dictionary<int, List<Assessment>>>();
            var term3ExamQuestionsBySubject = new Dictionary<int, List<AssessmentQuestion>>();
            var term3ExamMaxMark = TypeWeights.First(t => t.Name == "Exam").MaxMark;

            int subjectProgress = 0;
            foreach (var subject in allSubjects)
            {
                assessmentsBySubjectTerm[subject.SubjectId] = new Dictionary<int, List<Assessment>>();

                foreach (var termWindow in Terms)
                {
                    var typeEntities = new Dictionary<string, AssessmentType>();
                    foreach (var (name, weight, _) in TypeWeights)
                    {
                        var at = new AssessmentType
                        {
                            SubjectId = subject.SubjectId,
                            Name = name,
                            WeightPercentage = weight,
                            Term = termWindow.Term
                        };
                        context.AssessmentTypes.Add(at);
                        typeEntities[name] = at;
                    }
                    await context.SaveChangesAsync();

                    await weightingService.CreateSimpleWeighting(subject.SubjectId, termWindow.Term,
                        TypeWeights.ToDictionary(t => typeEntities[t.Name].AssessmentTypeId, t => t.Weight));

                    var termLength = (termWindow.End - termWindow.Start).Days;
                    var assessmentList = new List<Assessment>();
                    foreach (var (name, _, maxMark) in TypeWeights)
                    {
                        var offsetDays = name switch
                        {
                            "Assignment" => (int)(termLength * 0.25),
                            "Test" => (int)(termLength * 0.55),
                            _ => (int)(termLength * 0.9) // Exam
                        };
                        var assessment = new Assessment
                        {
                            SubjectId = subject.SubjectId,
                            AssessmentTypeId = typeEntities[name].AssessmentTypeId,
                            Title = $"{subject.Name} {name} - Term {termWindow.Term}",
                            MaxMark = maxMark,
                            Date = termWindow.Start.AddDays(offsetDays),
                            Term = termWindow.Term
                        };
                        context.Assessments.Add(assessment);
                        assessmentList.Add(assessment);
                    }
                    await context.SaveChangesAsync();
                    assessmentsBySubjectTerm[subject.SubjectId][termWindow.Term] = assessmentList;

                    if (termWindow.Term == 3)
                    {
                        var examAssessment = assessmentList[2]; // TypeWeights order: Assignment, Test, Exam
                        var questions = new List<AssessmentQuestion>();
                        for (int q = 1; q <= 5; q++)
                        {
                            var question = new AssessmentQuestion
                            {
                                AssessmentId = examAssessment.AssessmentId,
                                QuestionNumber = q,
                                Topic = $"{subject.Name} Topic {q}",
                                MaxMark = term3ExamMaxMark / 5m
                            };
                            context.AssessmentQuestions.Add(question);
                            questions.Add(question);
                        }
                        await context.SaveChangesAsync();
                        term3ExamQuestionsBySubject[subject.SubjectId] = questions;
                    }
                }

                subjectProgress++;
                if (subjectProgress % 5 == 0)
                    Console.WriteLine($"[DemoDataSeeder] Assessment structure: {subjectProgress}/{allSubjects.Count} subjects done.");
            }
            Console.WriteLine("[DemoDataSeeder] All assessment types, weightings, assessments, and Term 3 exam questions created.");

            // ---- Learner marks, question marks, interventions, and attendance ----
            int totalMarks = 0, totalInterventionCalls = 0, totalAttendanceRecords = 0;
            subjectProgress = 0;

            foreach (var subject in allSubjects)
            {
                var enrolledLearnerIds = learnersBySubject[subject.SubjectId];

                foreach (var termWindow in Terms)
                {
                    foreach (var assessment in assessmentsBySubjectTerm[subject.SubjectId][termWindow.Term])
                    {
                        foreach (var learnerId in enrolledLearnerIds)
                        {
                            var (ability, _) = learnerTraits[learnerId];
                            bool isAbsent = rng.NextDouble() < 0.03;
                            decimal marksAwarded = 0m;
                            if (!isAbsent)
                            {
                                double fraction = Math.Clamp(ability + (rng.NextDouble() - 0.5) * 0.25, 0, 1);
                                marksAwarded = Math.Round((decimal)fraction * assessment.MaxMark, 2);
                            }
                            context.LearnerMarks.Add(new LearnerMark
                            {
                                AssessmentId = assessment.AssessmentId,
                                LearnerId = learnerId,
                                MarksAwarded = marksAwarded,
                                IsAbsent = isAbsent
                            });
                            totalMarks++;
                        }
                    }
                }
                await context.SaveChangesAsync();

                // Term 3 exam question-level marks + real interventions (reuses InterventionService
                // so Interventions rows come from the actual thresholds/messages, not a duplicate copy)
                if (term3ExamQuestionsBySubject.TryGetValue(subject.SubjectId, out var questions))
                {
                    var questionMarksForInterventions = new List<(int LearnerId, int QuestionId, decimal MarksAwarded, decimal MaxMark)>();

                    foreach (var learnerId in enrolledLearnerIds)
                    {
                        var (ability, _) = learnerTraits[learnerId];
                        foreach (var question in questions)
                        {
                            double fraction = Math.Clamp(ability + (rng.NextDouble() - 0.5) * 0.3, 0, 1);
                            decimal marksAwarded = Math.Round((decimal)fraction * question.MaxMark, 2);

                            context.LearnerQuestionMarks.Add(new LearnerQuestionMark
                            {
                                QuestionId = question.QuestionId,
                                LearnerId = learnerId,
                                MarksAwarded = marksAwarded
                            });
                            questionMarksForInterventions.Add((learnerId, question.QuestionId, marksAwarded, question.MaxMark));
                        }
                    }
                    await context.SaveChangesAsync();

                    foreach (var (learnerId, questionId, marksAwarded, maxMark) in questionMarksForInterventions)
                    {
                        await interventionService.GenerateInterventions(learnerId, questionId, marksAwarded, maxMark);
                        totalInterventionCalls++;
                    }
                }

                // Attendance: biweekly sessions across the 3 terms
                var sessionDates = new List<DateTime>();
                foreach (var termWindow in Terms)
                {
                    for (var date = termWindow.Start; date <= termWindow.End; date = date.AddDays(14))
                        sessionDates.Add(date);
                }

                foreach (var date in sessionDates)
                {
                    var session = new AttendanceSession
                    {
                        SubjectId = subject.SubjectId,
                        Date = date,
                        Time = new TimeOnly(8, 0),
                        TeacherId = subject.TeacherId
                    };
                    context.AttendanceSessions.Add(session);
                    await context.SaveChangesAsync();

                    foreach (var learnerId in enrolledLearnerIds)
                    {
                        var (_, reliability) = learnerTraits[learnerId];
                        context.AttendanceRecords.Add(new AttendanceRecord
                        {
                            SessionId = session.SessionId,
                            LearnerId = learnerId,
                            Status = SampleStatus(reliability, rng)
                        });
                        totalAttendanceRecords++;
                    }
                }
                await context.SaveChangesAsync();

                subjectProgress++;
                Console.WriteLine($"[DemoDataSeeder] Marks/interventions/attendance: {subjectProgress}/{allSubjects.Count} subjects done ({subject.Name} Grade {subject.Grade}).");
            }

            Console.WriteLine("[DemoDataSeeder] Done.");
            Console.WriteLine($"[DemoDataSeeder] Summary: 1 school, {SubjectCatalog.Length} teachers, {learnerTraits.Count} learners, {parentCount} parents, {allSubjects.Count} subjects, {totalMarks} marks, {totalInterventionCalls} interventions generated, {totalAttendanceRecords} attendance records.");
            Console.WriteLine($"[DemoDataSeeder] School code: {SchoolCode}. Passwords — Admin: Admin@1234, Teacher: Teacher@1234, Learner: Learner@1234, Parent: Parent@1234.");
            Console.WriteLine($"[DemoDataSeeder] Sample admin: {adminEmail}");
            foreach (var (email, fullname) in teacherSample)
                Console.WriteLine($"[DemoDataSeeder] Sample teacher: {email} ({fullname})");
            foreach (var (email, fullname, hint) in learnerSample)
                Console.WriteLine($"[DemoDataSeeder] Sample learner: {email} ({fullname}) — {hint}");
        }

        private static (string First, string Last) RandomName(Random rng) =>
            (FirstNames[rng.Next(FirstNames.Length)], LastNames[rng.Next(LastNames.Length)]);

        private static string MakeUniqueEmail(string first, string last, string domain, HashSet<string> used)
        {
            static string Slug(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

            var baseLocal = $"{Slug(first)}.{Slug(last)}";
            var candidate = $"{baseLocal}@{domain}";
            int suffix = 1;
            while (!used.Add(candidate))
            {
                candidate = $"{baseLocal}{suffix}@{domain}";
                suffix++;
            }
            return candidate;
        }

        private static List<T> PickRandom<T>(List<T> pool, int count, Random rng)
        {
            var copy = new List<T>(pool);
            var result = new List<T>();
            for (int i = 0; i < count && copy.Count > 0; i++)
            {
                int idx = rng.Next(copy.Count);
                result.Add(copy[idx]);
                copy.RemoveAt(idx);
            }
            return result;
        }

        // Mixture distribution so the school has a realistic spread of performers rather
        // than everyone clustering near the mean: ~15% struggling, ~20% below-average,
        // ~45% average, ~20% strong.
        private static double SampleAbility(Random rng)
        {
            double roll = rng.NextDouble();
            if (roll < 0.15) return 0.20 + rng.NextDouble() * 0.20;
            if (roll < 0.35) return 0.40 + rng.NextDouble() * 0.15;
            if (roll < 0.80) return 0.55 + rng.NextDouble() * 0.20;
            return 0.75 + rng.NextDouble() * 0.20;
        }

        private static string SampleStatus(double reliability, Random rng)
        {
            if (rng.NextDouble() < reliability) return "Present";
            return rng.NextDouble() < 0.4 ? "Late" : "Absent";
        }
    }
}
