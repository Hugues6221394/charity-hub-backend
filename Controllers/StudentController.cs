using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StudentCharityHub.Models;
using StudentCharityHub.Repositories;
using StudentCharityHub.Services;
using System.Security.Claims;

namespace StudentCharityHub.Controllers
{
    [Authorize(Roles = "Admin,Student")]
    public class StudentController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<StudentController> _logger;

        public StudentController(
            IUnitOfWork unitOfWork,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<StudentController> logger)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _environment = environment;
            _logger = logger;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var students = await _unitOfWork.Students.FindAsync(s => s.IsVisible);
            return View(students.OrderByDescending(s => s.CreatedAt).ToList());
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(id);
            if (student == null || (!student.IsVisible && !User.IsInRole("Admin")))
            {
                return NotFound();
            }

            var donations = await _unitOfWork.Donations.FindAsync(d => d.StudentId == id && d.Status == "Completed");
            var progressReports = await _unitOfWork.ProgressReports.FindAsync(pr => pr.StudentId == id);
            var documents = await _unitOfWork.Documents.FindAsync(d => d.StudentId == id);

            ViewBag.Donations = donations.OrderByDescending(d => d.CreatedAt).ToList();
            ViewBag.ProgressReports = progressReports.OrderByDescending(pr => pr.ReportDate).ToList();
            ViewBag.Documents = documents.ToList();

            return View(student);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            _logger.LogInformation("Admin requested student creation form.");
            await PopulateStudentUsersAsync();
            return View();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Student student, IFormFile? photo, List<IFormFile>? documents)
        {
            _logger.LogInformation("Admin submitted student creation form for user {UserId}.", student.ApplicationUserId);

            // Navigation property shouldn't participate in validation when binding
            ModelState.Remove(nameof(student.ApplicationUser));

            if (string.IsNullOrWhiteSpace(student.ApplicationUserId))
            {
                ModelState.AddModelError(nameof(student.ApplicationUserId), "Please select a user to link with this student.");
            }
            else
            {
                var linkedUser = await _userManager.FindByIdAsync(student.ApplicationUserId);
                if (linkedUser == null)
                {
                    _logger.LogWarning("Student creation failed because user {UserId} was not found.", student.ApplicationUserId);
                    ModelState.AddModelError(nameof(student.ApplicationUserId), "Selected user no longer exists.");
                }
                else
                {
                    var existingProfile = await _unitOfWork.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == student.ApplicationUserId);
                    if (existingProfile != null)
                    {
                        _logger.LogWarning("Student creation failed because user {UserId} already has a student profile (StudentId {StudentId}).", student.ApplicationUserId, existingProfile.Id);
                        ModelState.AddModelError(nameof(student.ApplicationUserId), "This user already has an associated student profile.");
                    }
                    else if (string.IsNullOrWhiteSpace(student.FullName))
                    {
                        student.FullName = $"{linkedUser.FirstName} {linkedUser.LastName}".Trim();
                    }
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    if (photo != null && photo.Length > 0)
                    {
                        var photoPath = await SaveFileAsync(photo, "images");
                        student.PhotoUrl = photoPath;
                    }

                    student.CreatedAt = DateTime.UtcNow;
                    student.UpdatedAt = DateTime.UtcNow;

                    await _unitOfWork.Students.AddAsync(student);
                    await _unitOfWork.SaveChangesAsync();

                    if (documents != null && documents.Any())
                    {
                        foreach (var doc in documents)
                        {
                            if (doc.Length > 0)
                            {
                                var docPath = await SaveFileAsync(doc, "documents");
                                var document = new Document
                                {
                                    StudentId = student.Id,
                                    FileName = doc.FileName,
                                    FilePath = docPath,
                                    FileType = Path.GetExtension(doc.FileName),
                                    FileSize = doc.Length,
                                    UploadedAt = DateTime.UtcNow
                                };
                                await _unitOfWork.Documents.AddAsync(document);
                            }
                        }
                        await _unitOfWork.SaveChangesAsync();
                    }

                    _logger.LogInformation("Student {StudentId} created successfully for user {UserId}.", student.Id, student.ApplicationUserId);
                    TempData["SuccessMessage"] = "Student created successfully.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while creating student for user {UserId}.", student.ApplicationUserId);
                    ModelState.AddModelError(string.Empty, "An error occurred while creating the student. Please try again.");
                }
            }
            else
            {
                LogModelErrors();
            }

            await PopulateStudentUsersAsync();
            return View(student);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            return View(student);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Student student, IFormFile? photo)
        {
            if (id != student.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var existingStudent = await _unitOfWork.Students.GetByIdAsync(id);
                if (existingStudent == null)
                {
                    return NotFound();
                }

                // Handle photo upload
                if (photo != null && photo.Length > 0)
                {
                    var photoPath = await SaveFileAsync(photo, "images");
                    existingStudent.PhotoUrl = photoPath;
                }

                existingStudent.FullName = student.FullName;
                existingStudent.Age = student.Age;
                existingStudent.Location = student.Location;
                existingStudent.Story = student.Story;
                existingStudent.AcademicBackground = student.AcademicBackground;
                existingStudent.DreamCareer = student.DreamCareer;
                existingStudent.FundingGoal = student.FundingGoal;
                existingStudent.IsVisible = student.IsVisible;
                existingStudent.UpdatedAt = DateTime.UtcNow;

                _unitOfWork.Students.Update(existingStudent);
                await _unitOfWork.SaveChangesAsync();

                TempData["SuccessMessage"] = "Student updated successfully.";
                return RedirectToAction("Index");
            }

            return View(student);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            _unitOfWork.Students.Remove(student);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Student deleted successfully.";
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var student = await _unitOfWork.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == userId);
            if (student == null)
            {
                return NotFound();
            }

            var donations = await _unitOfWork.Donations.FindAsync(d => d.StudentId == student.Id && d.Status == "Completed");
            var followers = await _unitOfWork.Follows.FindAsync(f => f.StudentId == student.Id);
            var documents = await _unitOfWork.Documents.FindAsync(d => d.StudentId == student.Id);

            // Load donor information for followers
            var followersWithDonors = new List<Follow>();
            foreach (var follow in followers)
            {
                var donor = await _userManager.FindByIdAsync(follow.DonorId);
                if (donor != null)
                {
                    follow.Donor = donor;
                    followersWithDonors.Add(follow);
                }
            }

            ViewBag.Donations = donations.ToList();
            ViewBag.Followers = followersWithDonors;
            ViewBag.TotalRaised = donations.Sum(d => d.Amount);
            ViewBag.Documents = documents.ToList();

            return View(student);
        }

        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> MyDonors()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var student = await _unitOfWork.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == userId);
            if (student == null)
            {
                return NotFound();
            }

            var donations = await _unitOfWork.Donations.FindAsync(d => d.StudentId == student.Id && d.Status == "Completed");
            var donorIds = donations.Select(d => d.DonorId).Distinct().ToList();
            var donors = new List<ApplicationUser>();

            foreach (var donorId in donorIds)
            {
                var donor = await _userManager.FindByIdAsync(donorId);
                if (donor != null)
                {
                    donors.Add(donor);
                }
            }

            return View(donors);
        }

        private async Task<string> SaveFileAsync(IFormFile file, string folder)
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, folder);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/{folder}/{uniqueFileName}";
        }

        private async Task PopulateStudentUsersAsync()
        {
            var existingStudentUserIds = (await _unitOfWork.Students.GetAllAsync())
                .Select(s => s.ApplicationUserId)
                .ToHashSet();

            var studentUsers = await _userManager.GetUsersInRoleAsync("Student");
            var availableUsers = studentUsers
                .Where(u => !existingStudentUserIds.Contains(u.Id))
                .OrderBy(u => u.FirstName)
                .ThenBy(u => u.LastName)
                .ToList();

            ViewBag.StudentUsers = availableUsers;
        }

        private void LogModelErrors()
        {
            foreach (var entry in ModelState)
            {
                var errors = entry.Value?.Errors;
                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        _logger.LogWarning("ModelState error on '{Key}': {ErrorMessage}", entry.Key, error.ErrorMessage);
                    }
                }
            }
        }
    }
}



