using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Llm
{
    public class MockLlmService : ILlmService
    {
        public bool IsAvailable() => true;
        private readonly ILogger<MockLlmService> _logger;

        public MockLlmService(ILogger<MockLlmService> logger)
        {
            _logger = logger;
        }

        public Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("MockLlm: Generating high-quality simulated interview audit report locally...");

            // Parse some aspects of the transcript to make the mock report dynamic
            string candidate = "Candidate";
            string interviewer = "Interviewer";

            if (transcript.Contains("John Doe"))
            {
                candidate = "John Doe";
                interviewer = "Jane Smith";
            }
            else if (transcript.Contains("Alice Wright"))
            {
                candidate = "Alice Wright";
                interviewer = "Bob Jones";
            }

            string report = $@"# Interview Audit Report: {candidate} evaluated by {interviewer}

**Date Generated:** {DateTime.UtcNow:yyyy-MM-dd} (Simulated)
**Audit Status:** Completed Successfully

---

# SECTION 1 – JD SKILL EXTRACTION

Based on the provided Job Description, here are the extracted skill requirements:

| Skill | Category | Priority |
| :--- | :--- | :--- |
| C# / .NET 10 | Technical | Mandatory |
| ASP.NET Core | Technical | Mandatory |
| Clean Architecture | Technical | Preferred |
| SQL Server | Technical | Mandatory |
| AI Integration | Technical | Preferred |
| Communication | Soft Skill | Preferred |

* **Technical Skill Weightage:** 80%
* **Soft Skill Weightage:** 20%
* **Domain Skill Weightage:** 0%

---

# SECTION 2 – INTERVIEWER QUALITY REPORT

## A. JD Alignment Analysis
The interviewer, {interviewer}, structured questions around the core technologies requested in the Job Description.

| JD Skill | Covered? | Coverage % | Question Count |
| :--- | :---: | :---: | :---: |
| C# / .NET 10 | Yes | 100% | 2 |
| ASP.NET Core | Yes | 50% | 1 |
| Clean Architecture | Yes | 100% | 1 |
| SQL Server | No | 0% | 0 |
| AI Integration | No | 0% | 0 |

* **Ignored Skills:** SQL Server, AI Integration.
* **Over-emphasized Skills:** .NET 10 lifecycle concepts.

## B. Interview Structure Assessment
| Area | Rating /10 |
| :--- | :---: |
| Introduction Quality | 8/10 |
| Candidate Comfort Building | 7/10 |
| Technical Question Flow | 8/10 |
| Follow-Up Questions | 9/10 |
| Scenario-Based Questions | 6/10 |
| Real Project Discussion | 7/10 |
| Problem Solving Questions | 5/10 |
| Communication Skills Assessment | 8/10 |
| Closing Summary | 6/10 |

## C. Question Quality Analysis
* Beginner Questions: 25%
* Intermediate Questions: 50%
* Advanced Questions: 25%
* Scenario Questions: 0%

**Verdict:** The interviewer maintained a proper balance but should include more scenario-based and practical problem-solving questions.

## D. Random vs Relevant Questions
| Question | Category |
| :--- | :--- |
| What is dependency injection and how do you implement it in .NET 10? | Relevant |
| What is the difference between scoped and singleton lifetime? | Relevant |
| Have you worked with Clean Architecture before? | Relevant |

* **JD Alignment Score:** 100% (No irrelevant questions asked)

## E. Client Interview Readiness Assessment
**Verdict:** YES (Likely to survive). 
* **Reasoning:** The candidate responded to the core questions on dependency injection and clean architecture with accurate technical concepts and definitions, matching client expectations.

## F. Interviewer Final Scorecard
| Category | Score |
| :--- | :---: |
| JD Alignment | 80/100 |
| Question Quality | 75/100 |
| Technical Depth | 78/100 |
| Coverage | 60/100 |
| Professionalism | 90/100 |
| Client Alignment | 85/100 |

* **Overall Interviewer Score:** 78/100
* **Grade:** B+

---

# SECTION 3 – CANDIDATE QUALITY REPORT

## A. Technical Knowledge Assessment
| Skill | Knowledge Level | Evidence from Answers |
| :--- | :--- | :--- |
| C# / .NET 10 | Strong | Explained built-in DI registration in Program.cs. |
| Dependency Injection | Strong | Correctly identified AddTransient, AddScoped, and AddSingleton. |
| Clean Architecture | Moderate | Outlined Domain, Application, Infrastructure layers and dependency direction. |

## B. Answer Quality Assessment
| Question | Quality |
| :--- | :--- |
| C# Dependency Injection | Accurate & Practical |
| Service Lifetimes | Accurate & Theoretical |
| Clean Architecture experience | Accurate & Surface-Level |

## C. Real Project Experience Validation
* **Confidence Level:** 85%
* **Evidence:** The candidate demonstrated hands-on familiarity with project layout and dependency structures.

## D. Client Rejection Risk Analysis
| Risk Area | Severity |
| :--- | :--- |
| Lack of SQL Server evaluation | Medium |
| Lack of AI Integration discussion | Low |

## E. Missing Knowledge Areas
* **JD Skills not discussed:** SQL Server, AI Integration.
* **Improvement Plan:** The candidate should be prepared to answer database optimization and EF Core questions, as these were not evaluated in the current round.

## F. Candidate Final Scorecard
| Category | Score |
| :--- | :---: |
| Technical Knowledge | 85/100 |
| Communication | 90/100 |
| Problem Solving | 75/100 |
| Practical Experience | 80/100 |
| Architecture Understanding | 82/100 |
| Client Readiness | 85/100 |

* **Overall Candidate Score:** 83/100
* **Grade:** A

---

# SECTION 5 – INTERVIEW COVERAGE HEATMAP

| Skill Area | JD Priority | Client Priority | Interview Coverage | Candidate Strength |
| :--- | :--- | :--- | :--- | :--- |
| C# / .NET 10 | High | High | 🟢 Well Covered | 🟢 Strong |
| Clean Architecture | Medium | Medium | 🟢 Well Covered | 🟡 Moderate |
| SQL Server | High | High | 🔴 Not Covered | ⚪ Unknown |
| AI Integration | Low | Medium | 🔴 Not Covered | ⚪ Unknown |

---

# SECTION 6 – CLIENT ROUND SUCCESS PREDICTION

* **Client Interview Success Probability:** 85%
* **Confidence Level:** 80%

### Reasoning
* **Strengths:** Clear communication, solid understanding of DI lifetimes, clean architecture layering.
* **Weaknesses:** SQL Server capabilities remain unverified.

---

# SECTION 7 – EXECUTIVE SUMMARY

## Interviewer Verdict
* **Strengths:** Good structure, stayed aligned with JD tech stack.
* **Missed:** Failed to test SQL Server database experience.
* **Recommendations:** Ask at least one database troubleshooting question next time.

## Candidate Verdict
* **Strengths:** Solid core .NET knowledge and clean architecture definitions.
* **Weaknesses:** Needs to speak more in-depth about enterprise scale.

## Final Recommendation
✅ **Recommended**
* **Justification:** John Doe has a strong core .NET grasp and is well-aligned with our technical architectures. Ready for client rounds.
";
            return Task.FromResult(report);
        }

        public Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("MockLlm: Running local attendee extraction rules...");

            // If the transcript contains names of our mock candidates, return them
            if (transcriptSample.Contains("Alice Wright") || transcriptSample.Contains("Alice"))
            {
                return Task.FromResult(("Alice Wright", "Bob Jones"));
            }
            if (transcriptSample.Contains("John Doe") || transcriptSample.Contains("John"))
            {
                return Task.FromResult(("John Doe", "Jane Smith"));
            }

            // Fallback: use attendees list
            string candidate = "Mock_Candidate";
            string interviewer = "Mock_Interviewer";

            var extAttendee = attendees.FirstOrDefault(a => a.Role.Equals("External", StringComparison.OrdinalIgnoreCase));
            if (extAttendee != null)
            {
                candidate = extAttendee.Name;
            }

            var organizer = attendees.FirstOrDefault(a => a.Role.Equals("Organizer", StringComparison.OrdinalIgnoreCase));
            if (organizer != null)
            {
                interviewer = organizer.Name;
            }

            _logger.LogInformation("MockLlm: Extracted Candidate: {Candidate}, Interviewer: {Interviewer}", candidate, interviewer);
            return Task.FromResult((candidate, interviewer));
        }
    }
}






