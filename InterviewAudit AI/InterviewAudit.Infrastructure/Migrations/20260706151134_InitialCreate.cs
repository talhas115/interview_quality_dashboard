using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InterviewAudit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MeetingId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CandidateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CandidateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InterviewerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDateTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EndDateTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CandidateScore = table.Column<int>(type: "int", nullable: true),
                    InterviewerScore = table.Column<int>(type: "int", nullable: true),
                    JdAlignmentScore = table.Column<int>(type: "int", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMeetings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMeetings_GroupId_MeetingId",
                table: "ProcessedMeetings",
                columns: new[] { "GroupId", "MeetingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedMeetings");
        }
    }
}
