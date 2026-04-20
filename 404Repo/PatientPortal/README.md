# Patient Portal - Medical Imaging System

A secure web portal for patients to access their medical files uploaded through the 403 Desktop Application.

## Features

- **Patient Authentication**: Secure login/registration with BCrypt password hashing
- **File Access**: View and download medical files (DICOM, PDF, images)
- **Encrypted Storage**: All files are encrypted at rest
- **Responsive Design**: Works on desktop and mobile devices
- **Role-Based Access**: Patients can only access their own files

## Project Structure

```
PatientPortal/
├── Controllers/
│   ├── AccountController.cs      # Login, Register, Logout
│   ├── FilesController.cs         # View and download files
│   └── HomeController.cs          # Dashboard and home
├── Models/
│   ├── Patient.cs                 # Patient entity
│   ├── PatientFile.cs             # File entity
│   └── ViewModels.cs              # Login, Register models
├── Services/
│   ├── IPatientAuthService.cs     # Auth interface
│   ├── PatientAuthService.cs      # Auth implementation
│   ├── IPatientFileService.cs     # File service interface
│   ├── PatientFileService.cs      # File service implementation
│   └── StorageServices.cs         # File storage & encryption
├── Data/
│   └── ApplicationDbContext.cs    # EF Core DbContext
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml           # Landing page
│   │   └── Dashboard.cshtml       # Patient dashboard
│   ├── Account/
│   │   ├── Login.cshtml           # Login form
│   │   ├── Register.cshtml        # Registration form
│   │   └── ChangePassword.cshtml  # Change password
│   ├── Files/
│   │   ├── Index.cshtml           # File list
│   │   └── View.cshtml            # File viewer
│   └── Shared/
│       ├── _Layout.cshtml         # Main layout
│       └── _LoginPartial.cshtml   # Login status
├── wwwroot/
│   ├── css/
│   │   └── site.css               # Custom styles
│   └── js/
│       └── site.js                # Custom scripts
├── appsettings.json               # Configuration
├── Program.cs                     # Application startup
└── PatientPortal.csproj           # Project file
```

## Setup Instructions

### 1. Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- Access to the MedicalImaging.db database

### 2. Configuration

Update `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../MedicalImagingAPI/MedicalImaging.db"
  },
  "Storage": {
    "BasePath": "C:\\MedicalImagingStorage"
  },
  "Encryption": {
    "Key": "YOUR_ENCRYPTION_KEY_HERE",
    "IV": "YOUR_IV_HERE"
  }
}
```

**Important**: Use the SAME encryption keys as the API to decrypt files!

### 3. Add to Solution

Add the Patient Portal to your existing solution:

```bash
cd YourSolutionDirectory
dotnet sln add PatientPortal/PatientPortal.csproj
```

### 4. Restore Packages

```bash
cd PatientPortal
dotnet restore
```

### 5. Patient Registration

Patients need to register first. They'll need:
- **Patient ID** (MRN from the database)
- **Personal Information** (Name, DOB, Email)
- **Password** (for portal access)

### 6. Run the Portal

```bash
dotnet run
```

Or press F5 in Visual Studio.

The portal will be available at:
- HTTPS: `https://localhost:7001`
- HTTP: `http://localhost:5001`

## Security Features

1. **Password Hashing**: BCrypt with salt
2. **File Encryption**: AES-256 encryption at rest
3. **Authentication**: Cookie-based with secure settings
4. **Authorization**: Patients can only access their own files
5. **HTTPS**: Enforced in production

## Usage

### For Patients

1. **Register**:
   - Go to `/Account/Register`
   - Enter your Patient ID (Medical Record Number)
   - Fill in personal details
   - Create a password

2. **Login**:
   - Go to `/Account/Login`
   - Enter Patient ID and password
   - Click "Login"

3. **View Files**:
   - Navigate to "My Files"
   - See all uploaded documents
   - Click to download or view

4. **Change Password**:
   - Go to "Account Settings"
   - Enter current and new password

### For Administrators

Patients are created in the database by the desktop application.
To allow a patient to access the portal:

1. They must register using their existing Patient ID
2. Or you can pre-create a password hash for them

## Database Schema

The portal uses these tables from the main database:

**Patients**:
- Id (Guid, PK)
- PatientId (string, unique)
- FirstName, LastName
- DateOfBirth
- Email, Phone
- PasswordHash (for portal access)
- IsActive

**PatientFiles**:
- Id (Guid, PK)
- PatientId (Guid, FK)
- FileName
- FilePath (encrypted location)
- FileType, Category
- UploadedAt, UploadedBy

## Creating the Views

Create these Razor views in the Views folder:

### Shared/_Layout.cshtml
- Main layout with navigation
- Bootstrap 5 for styling
- Login/Logout links

### Home/Index.cshtml
- Landing page
- Links to Login/Register

### Home/Dashboard.cshtml
- Welcome message
- Quick stats (file count)
- Recent files

### Account/Login.cshtml
- Patient ID input
- Password input
- Remember me checkbox
- Link to register

### Account/Register.cshtml
- Patient ID
- Personal information
- Password creation

### Files/Index.cshtml
- Table of all files
- Download/View buttons
- Sort by date, type, category

### Files/View.cshtml
- Display file details
- PDF viewer for PDFs
- Image preview for images
- Download button

## Integration with Desktop App

The Patient Portal:
1. **Shares the database** with the API/Desktop App
2. **Reads encrypted files** from the same storage location
3. **Uses the same encryption keys** to decrypt files

Files uploaded via the Desktop App are automatically available in the portal!

## Troubleshooting

### "File not found"
- Check that `Storage:BasePath` matches the API configuration
- Verify file exists in the storage location

### "Cannot decrypt file"
- Ensure encryption keys match the API
- Check that file was uploaded with encryption

### "Patient ID not found"
- Patient must be registered via Desktop App first
- Patient ID is case-sensitive

### "Database locked"
- Only one application can write to SQLite at a time
- Close other connections before running

## Production Deployment

1. **Use HTTPS**: Configure SSL certificate
2. **Environment Variables**: Don't store keys in appsettings.json
3. **Logging**: Configure proper logging
4. **Error Pages**: Customize error pages
5. **Session Security**: Use Redis for distributed cache
6. **CORS**: Configure if needed

## Next Steps

- Add email verification for registration
- Implement "Forgot Password" functionality
- Add file upload from patient portal
- Implement appointment scheduling
- Add messaging with healthcare providers
- Mobile app version

## Support

For issues or questions:
- Check the main project documentation
- Review the API documentation
- Contact system administrator
