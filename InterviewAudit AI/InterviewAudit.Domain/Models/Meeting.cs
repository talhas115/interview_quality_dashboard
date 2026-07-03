using System;
using System.Collections.Generic;

namespace InterviewAudit.Domain.Models
{
    public class Meeting
    {
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BodyPreview { get; set; } = string.Empty;
        public string OrganizerName { get; set; } = string.Empty;
        public List<Attendee> Attendees { get; set; } = new List<Attendee>();
        public string JoinUrl { get; set; } = string.Empty;
        public DateTimeOffset? StartDateTime { get; set; }
        public DateTimeOffset? EndDateTime { get; set; }
        public string GroupId { get; set; } = string.Empty;
        public string CandidateId { get; set; } = string.Empty;
    }

    public class Attendee
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
