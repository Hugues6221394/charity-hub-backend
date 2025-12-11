using System.ComponentModel.DataAnnotations;
using StudentCharityHub.Models;

namespace StudentCharityHub.DTOs
{
    public class StudentApplicationSubmitDto
    {
        // Personal Information
        [Required]
        [StringLength(200)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Range(16, 100)]
        public int Age { get; set; }

        [Required]
        [StringLength(200)]
        public string PlaceOfBirth { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string CurrentResidency { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        public string? PhoneNumber { get; set; }

        // Family Information
        [Required]
        [StringLength(200)]
        public string FatherName { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string MotherName { get; set; } = string.Empty;

        [Required]
        [Range(0, 999999999)]
        public decimal ParentsAnnualSalary { get; set; }

        [StringLength(1000)]
        public string? FamilySituation { get; set; }

        // Academic Information
        [Required]
        [StringLength(2000, MinimumLength = 100)]
        public string PersonalStory { get; set; } = string.Empty;

        [Required]
        [StringLength(1000, MinimumLength = 50)]
        public string AcademicBackground { get; set; } = string.Empty;

        [StringLength(200)]
        public string? CurrentEducationLevel { get; set; }

        [StringLength(200)]
        public string? FieldOfStudy { get; set; }

        [StringLength(200)]
        public string? DreamCareer { get; set; }

        // Funding Information
        [Required]
        [Range(100, 999999)]
        public decimal RequestedFundingAmount { get; set; }

        [StringLength(1000)]
        public string? FundingPurpose { get; set; }

        // Files will be uploaded separately
        public string? ProfileImageUrl { get; set; }
        public List<string>? ProofDocumentUrls { get; set; }
        public List<string>? GalleryImageUrls { get; set; }
    }

    public class StudentApplicationDto
    {
        public int Id { get; set; }
        public string ApplicationUserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string PlaceOfBirth { get; set; } = string.Empty;
        public string CurrentResidency { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string FatherName { get; set; } = string.Empty;
        public string MotherName { get; set; } = string.Empty;
        public decimal ParentsAnnualSalary { get; set; }
        public string? FamilySituation { get; set; }
        public string PersonalStory { get; set; } = string.Empty;
        public string AcademicBackground { get; set; } = string.Empty;
        public string? CurrentEducationLevel { get; set; }
        public string? FieldOfStudy { get; set; }
        public string? DreamCareer { get; set; }
        public string? ProfileImageUrl { get; set; }
        public List<string>? ProofDocumentUrls { get; set; }
        public List<string>? GalleryImageUrls { get; set; }
        public decimal RequestedFundingAmount { get; set; }
        public string? FundingPurpose { get; set; }
        public ApplicationStatus Status { get; set; }
        public string? RejectionReason { get; set; }
        public string? ReviewedByManagerId { get; set; }
        public DateTime? ReviewedByManagerAt { get; set; }
        public string? ApprovedByAdminId { get; set; }
        public DateTime? ApprovedByAdminAt { get; set; }
        public bool IsPostedAsStudent { get; set; }
        public int? StudentId { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ApplicationActionDto
    {
        public string? Reason { get; set; }
    }

    public class PostStudentDto
    {
        [Required]
        public int ApplicationId { get; set; }

        // Optional overrides
        public decimal? FundingGoal { get; set; }
        public string? Location { get; set; }
    }

    public class UpdateStatusDto
    {
        [Required]
        public ApplicationStatus Status { get; set; }
        public string? Reason { get; set; }
    }
}
