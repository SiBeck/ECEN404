# Patient Portal - Complete Setup Guide

## Quick Start

1. Copy the entire PatientPortal folder to your solution directory
2. Add to your solution: `dotnet sln add PatientPortal/PatientPortal.csproj`
3. Update appsettings.json with your paths
4. Create the remaining view files (templates below)
5. Run: `dotnet run --project PatientPortal`

## Required View Files to Create

### Views/Home/Index.cshtml
```cshtml
@{
    ViewData["Title"] = "Welcome";
}

<div class="text-center">
    <h1 class="display-4">Welcome to Patient Portal</h1>
    <p class="lead">Secure access to your medical records</p>
    
    @if (User.Identity?.IsAuthenticated != true)
    {
        <div class="mt-5">
            <a asp-controller="Account" asp-action="Login" class="btn btn-primary btn-lg me-3">
                <i class="bi bi-box-arrow-in-right"></i> Login
            </a>
            <a asp-controller="Account" asp-action="Register" class="btn btn-outline-primary btn-lg">
                <i class="bi bi-person-plus"></i> Register
            </a>
        </div>
    }
</div>
```

### Views/Home/Dashboard.cshtml
```cshtml
@using System.Security.Claims
@{
    ViewData["Title"] = "Dashboard";
    var patientName = User.Identity?.Name ?? "Patient";
}

<div class="row">
    <div class="col-md-12">
        <h2>Welcome, @patientName!</h2>
        <p class="lead">Access your medical records and files</p>
    </div>
</div>

<div class="row mt-4">
    <div class="col-md-4">
        <div class="card">
            <div class="card-body text-center">
                <i class="bi bi-file-earmark-medical display-3 text-primary"></i>
                <h5 class="card-title mt-3">My Files</h5>
                <p class="card-text">View and download your medical records</p>
                <a asp-controller="Files" asp-action="Index" class="btn btn-primary">View Files</a>
            </div>
        </div>
    </div>
    
    <div class="col-md-4">
        <div class="card">
            <div class="card-body text-center">
                <i class="bi bi-person-circle display-3 text-success"></i>
                <h5 class="card-title mt-3">Account Settings</h5>
                <p class="card-text">Update your password and preferences</p>
                <a asp-controller="Account" asp-action="ChangePassword" class="btn btn-success">Settings</a>
            </div>
        </div>
    </div>
    
    <div class="col-md-4">
        <div class="card">
            <div class="card-body text-center">
                <i class="bi bi-shield-check display-3 text-info"></i>
                <h5 class="card-title mt-3">Privacy</h5>
                <p class="card-text">Learn about how we protect your data</p>
                <a asp-controller="Home" asp-action="Privacy" class="btn btn-info">Learn More</a>
            </div>
        </div>
    </div>
</div>
```

### Views/Account/Login.cshtml
```cshtml
@model LoginViewModel
@{
    ViewData["Title"] = "Login";
}

<div class="row justify-content-center">
    <div class="col-md-6 col-lg-5">
        <div class="card shadow-sm">
            <div class="card-body p-5">
                <h2 class="card-title text-center mb-4">Patient Login</h2>
                
                <form asp-action="Login" method="post">
                    <div asp-validation-summary="All" class="text-danger mb-3"></div>
                    
                    <div class="mb-3">
                        <label asp-for="PatientId" class="form-label"></label>
                        <input asp-for="PatientId" class="form-control" placeholder="Enter your Patient ID" />
                        <span asp-validation-for="PatientId" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="Password" class="form-label"></label>
                        <input asp-for="Password" class="form-control" placeholder="Enter your password" />
                        <span asp-validation-for="Password" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3 form-check">
                        <input asp-for="RememberMe" class="form-check-input" />
                        <label asp-for="RememberMe" class="form-check-label"></label>
                    </div>
                    
                    <button type="submit" class="btn btn-primary w-100 mb-3">Login</button>
                    
                    <div class="text-center">
                        <p>Don't have an account? <a asp-action="Register">Register here</a></p>
                    </div>
                </form>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

### Views/Account/Register.cshtml
```cshtml
@model RegisterViewModel
@{
    ViewData["Title"] = "Register";
}

<div class="row justify-content-center">
    <div class="col-md-8 col-lg-6">
        <div class="card shadow-sm">
            <div class="card-body p-5">
                <h2 class="card-title text-center mb-4">Patient Registration</h2>
                
                <form asp-action="Register" method="post">
                    <div asp-validation-summary="All" class="text-danger mb-3"></div>
                    
                    <div class="mb-3">
                        <label asp-for="PatientId" class="form-label"></label>
                        <input asp-for="PatientId" class="form-control" />
                        <span asp-validation-for="PatientId" class="text-danger"></span>
                        <small class="form-text text-muted">Your Medical Record Number (MRN)</small>
                    </div>
                    
                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label asp-for="FirstName" class="form-label"></label>
                            <input asp-for="FirstName" class="form-control" />
                            <span asp-validation-for="FirstName" class="text-danger"></span>
                        </div>
                        <div class="col-md-6 mb-3">
                            <label asp-for="LastName" class="form-label"></label>
                            <input asp-for="LastName" class="form-control" />
                            <span asp-validation-for="LastName" class="text-danger"></span>
                        </div>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="DateOfBirth" class="form-label"></label>
                        <input asp-for="DateOfBirth" class="form-control" type="date" />
                        <span asp-validation-for="DateOfBirth" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="Email" class="form-label"></label>
                        <input asp-for="Email" class="form-control" type="email" />
                        <span asp-validation-for="Email" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="Phone" class="form-label"></label>
                        <input asp-for="Phone" class="form-control" type="tel" />
                        <span asp-validation-for="Phone" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="Password" class="form-label"></label>
                        <input asp-for="Password" class="form-control" />
                        <span asp-validation-for="Password" class="text-danger"></span>
                    </div>
                    
                    <div class="mb-3">
                        <label asp-for="ConfirmPassword" class="form-label"></label>
                        <input asp-for="ConfirmPassword" class="form-control" />
                        <span asp-validation-for="ConfirmPassword" class="text-danger"></span>
                    </div>
                    
                    <button type="submit" class="btn btn-primary w-100 mb-3">Register</button>
                    
                    <div class="text-center">
                        <p>Already have an account? <a asp-action="Login">Login here</a></p>
                    </div>
                </form>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

### Views/Files/Index.cshtml
```cshtml
@model List<PatientFile>
@{
    ViewData["Title"] = "My Files";
}

<h2>My Medical Files</h2>

@if (!Model.Any())
{
    <div class="alert alert-info">
        <i class="bi bi-info-circle"></i> You don't have any files uploaded yet.
    </div>
}
else
{
    <div class="table-responsive">
        <table class="table table-striped table-hover">
            <thead class="table-dark">
                <tr>
                    <th>File Name</th>
                    <th>Type</th>
                    <th>Category</th>
                    <th>Size</th>
                    <th>Uploaded</th>
                    <th>Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var file in Model)
                {
                    <tr>
                        <td>
                            <i class="bi @(file.IsPdf ? "bi-file-earmark-pdf text-danger" : file.IsImage ? "bi-file-earmark-image text-success" : file.IsDicom ? "bi-file-earmark-medical text-primary" : "bi-file-earmark")"></i>
                            @file.FileName
                        </td>
                        <td>@file.FileType</td>
                        <td>@file.Category</td>
                        <td>@file.FileSizeFormatted</td>
                        <td>@file.UploadedAt.ToString("MM/dd/yyyy")</td>
                        <td>
                            <a asp-action="Download" asp-route-id="@file.Id" class="btn btn-sm btn-primary">
                                <i class="bi bi-download"></i> Download
                            </a>
                            @if (file.IsPdf || file.IsImage)
                            {
                                <a asp-action="View" asp-route-id="@file.Id" class="btn btn-sm btn-info">
                                    <i class="bi bi-eye"></i> View
                                </a>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

### Views/Home/Privacy.cshtml
```cshtml
@{
    ViewData["Title"] = "Privacy Policy";
}
<h1>@ViewData["Title"]</h1>

<p>Your privacy is important to us. This privacy statement explains what personal data we collect and how we use it.</p>

<h3>Information We Collect</h3>
<ul>
    <li>Patient identification information</li>
    <li>Medical records and files</li>
    <li>Contact information</li>
</ul>

<h3>How We Use Your Information</h3>
<ul>
    <li>To provide access to your medical records</li>
    <li>To communicate with you about your account</li>
    <li>To improve our services</li>
</ul>

<h3>Security</h3>
<p>All files are encrypted and stored securely. We use industry-standard security measures to protect your data.</p>
```

### Views/Shared/_ValidationScriptsPartial.cshtml
```cshtml
<script src="https://cdn.jsdelivr.net/npm/jquery-validation@1.19.5/dist/jquery.validate.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js"></script>
```

## CSS (wwwroot/css/site.css)
```css
:root {
    --primary-color: #0d6efd;
    --secondary-color: #6c757d;
}

html {
    font-size: 14px;
    position: relative;
    min-height: 100%;
}

body {
    margin-bottom: 60px;
}

.footer {
    position: absolute;
    bottom: 0;
    width: 100%;
    white-space: nowrap;
    line-height: 60px;
}

.card {
    margin-bottom: 1.5rem;
    border-radius: 0.5rem;
}

.card:hover {
    transform: translateY(-5px);
    transition: transform 0.3s ease;
    box-shadow: 0 4px 8px rgba(0,0,0,0.1);
}

.table-hover tbody tr:hover {
    background-color: rgba(0,0,0,0.05);
}

.bi {
    display: inline-block;
    vertical-align: middle;
}
```

## JavaScript (wwwroot/js/site.js)
```javascript
// Add any custom JavaScript here
console.log('Patient Portal loaded');

// Auto-hide alerts after 5 seconds
document.addEventListener('DOMContentLoaded', function() {
    setTimeout(function() {
        var alerts = document.querySelectorAll('.alert');
        alerts.forEach(function(alert) {
            var bsAlert = new bootstrap.Alert(alert);
            bsAlert.close();
        });
    }, 5000);
});
```

## Run the Application

1. Navigate to the PatientPortal directory
2. Run: `dotnet restore`
3. Run: `dotnet run`
4. Open browser to `https://localhost:7001`

## Testing

1. Register a new patient account
2. Login with Patient ID and password
3. View the dashboard
4. Navigate to "My Files" to see uploaded files
5. Download or view files

## Troubleshooting

- **Can't connect to database**: Check connection string in appsettings.json
- **Files won't decrypt**: Verify encryption keys match the API
- **Can't login**: Ensure patient is registered and active

Enjoy your Patient Portal!
