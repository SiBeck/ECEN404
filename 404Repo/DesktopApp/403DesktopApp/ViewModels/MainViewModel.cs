using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using _403DesktopApp.Models;
using _403DesktopApp.Services;

namespace _403DesktopApp
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _currentPage = "Dashboard";
        private string _userInput = "";
        private string _statusMessage = "";
        private string _statusText = "Ready";
        private BitmapSource _currentImageSource;
        private double _zoomLevel = 1.0;
        private string _imageInfo = "No image loaded";
        private string _imageDimensions = "";
        private string _currentImagePath = "";
        private DicomImage _currentDicomImage;
        private int _currentFrameIndex = 0;
        private int _totalFrames = 0;

        // Scan filter fields
        private readonly ScanFilterService _scanFilterService = new();
        private bool _isFilterRunning;
        private string _filterResultSummary = "";
        private List<string> _filteredDicomFiles = new();
        private bool _isFilteredSeriesMode;

        // Cardiac gating fields
        private readonly CardiacGatingService _cardiacGatingService = new();
        private bool _isGatingRunning;
        private string _gatingResultSummary = "";
        private string _selectedCsvPath = "";
        private string _selectedMrdPath = "";

        // Patient management fields
        private readonly PatientService _patientService = new();
        private ObservableCollection<PatientProfile> _patients = new();
        private PatientProfile? _selectedPatient;
        private string _patientSearchQuery = "";
        private string _patientStatusMessage = "";
        private bool _isEditingPatient;
        // Patient form fields
        private string _patFirstName = "";
        private string _patLastName = "";
        private DateTime _patDateOfBirth = DateTime.Today;
        private string _patGender = "";
        private string _patMrn = "";
        private string _patPhone = "";
        private string _patEmail = "";
        private string _patAddress = "";
        private string _patEmergencyName = "";
        private string _patEmergencyPhone = "";
        private string _patNotes = "";

        // Patient picker overlay fields (shown from Image Viewer "Save to Patient File")
        private bool _isPatientPickerVisible;
        private string _pickerSearchQuery = "";
        private ObservableCollection<PatientProfile> _pickerPatients = new();
        private PatientProfile? _pickerSelectedPatient;
        private string _imageTag = "";
        private string _imageDescription = "";
        private string _imageSourceType = "Manual";

        // Patient images display fields (shown in Patients tab)
        private ObservableCollection<PatientImage> _selectedPatientImages = new();
        private PatientImage? _selectedPatientImage;
        private string _editImageTag = "";
        private string _editImageDescription = "";

        public string CurrentPage
        {
            get => _currentPage;
            set { _currentPage = value; OnPropertyChanged(); }
        }

        public string UserInput
        {
            get => _userInput;
            set { _userInput = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public BitmapSource CurrentImageSource
        {
            get => _currentImageSource;
            set
            {
                _currentImageSource = value;
                OnPropertyChanged();
                UpdateImageInfo();
            }
        }

        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                _zoomLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ZoomPercentage));
            }
        }

        public double ZoomPercentage => _zoomLevel * 100;

        public string ImageInfo
        {
            get => _imageInfo;
            set { _imageInfo = value; OnPropertyChanged(); }
        }

        public string ImageDimensions
        {
            get => _imageDimensions;
            set { _imageDimensions = value; OnPropertyChanged(); }
        }

        public int CurrentFrameIndex
        {
            get => _currentFrameIndex;
            set
            {
                _currentFrameIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FrameInfo));
            }
        }

        public int TotalFrames
        {
            get => _totalFrames;
            set
            {
                _totalFrames = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FrameInfo));
                OnPropertyChanged(nameof(HasMultipleFrames));
            }
        }

        public string FrameInfo => TotalFrames > 1 ? $"Frame {CurrentFrameIndex + 1} of {TotalFrames}" : "";

        public bool HasMultipleFrames => TotalFrames > 1;

        // Scan filter properties
        public bool IsFilterRunning
        {
            get => _isFilterRunning;
            set { _isFilterRunning = value; OnPropertyChanged(); }
        }

        public string FilterResultSummary
        {
            get => _filterResultSummary;
            set { _filterResultSummary = value; OnPropertyChanged(); }
        }

        // Cardiac gating properties
        public bool IsGatingRunning
        {
            get => _isGatingRunning;
            set { _isGatingRunning = value; OnPropertyChanged(); }
        }

        public string GatingResultSummary
        {
            get => _gatingResultSummary;
            set { _gatingResultSummary = value; OnPropertyChanged(); }
        }

        public string SelectedCsvPath
        {
            get => _selectedCsvPath;
            set
            {
                _selectedCsvPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCsvFileName));
                OnPropertyChanged(nameof(CanRunGating));
            }
        }

        public string SelectedMrdPath
        {
            get => _selectedMrdPath;
            set
            {
                _selectedMrdPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedMrdFileName));
                OnPropertyChanged(nameof(CanRunGating));
            }
        }

        public string SelectedCsvFileName =>
            string.IsNullOrEmpty(_selectedCsvPath) ? "No file selected" : Path.GetFileName(_selectedCsvPath);

        public string SelectedMrdFileName =>
            string.IsNullOrEmpty(_selectedMrdPath) ? "No file selected" : Path.GetFileName(_selectedMrdPath);

        public bool CanRunGating =>
            !_isGatingRunning
            && !string.IsNullOrEmpty(_selectedCsvPath)
            && !string.IsNullOrEmpty(_selectedMrdPath);

        // Patient management properties
        public ObservableCollection<PatientProfile> Patients
        {
            get => _patients;
            set { _patients = value; OnPropertyChanged(); }
        }

        public PatientProfile? SelectedPatient
        {
            get => _selectedPatient;
            set
            {
                _selectedPatient = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedPatient));
                if (value != null) LoadPatientIntoForm(value);
            }
        }

        public bool HasSelectedPatient => _selectedPatient != null;

        public string PatientSearchQuery
        {
            get => _patientSearchQuery;
            set { _patientSearchQuery = value; OnPropertyChanged(); }
        }

        public string PatientStatusMessage
        {
            get => _patientStatusMessage;
            set { _patientStatusMessage = value; OnPropertyChanged(); }
        }

        public bool IsEditingPatient
        {
            get => _isEditingPatient;
            set { _isEditingPatient = value; OnPropertyChanged(); }
        }

        public string PatFirstName
        {
            get => _patFirstName;
            set { _patFirstName = value; OnPropertyChanged(); }
        }

        public string PatLastName
        {
            get => _patLastName;
            set { _patLastName = value; OnPropertyChanged(); }
        }

        public DateTime PatDateOfBirth
        {
            get => _patDateOfBirth;
            set { _patDateOfBirth = value; OnPropertyChanged(); }
        }

        public string PatGender
        {
            get => _patGender;
            set { _patGender = value; OnPropertyChanged(); }
        }

        public string PatMrn
        {
            get => _patMrn;
            set { _patMrn = value; OnPropertyChanged(); }
        }

        public string PatPhone
        {
            get => _patPhone;
            set { _patPhone = value; OnPropertyChanged(); }
        }

        public string PatEmail
        {
            get => _patEmail;
            set { _patEmail = value; OnPropertyChanged(); }
        }

        public string PatAddress
        {
            get => _patAddress;
            set { _patAddress = value; OnPropertyChanged(); }
        }

        public string PatEmergencyName
        {
            get => _patEmergencyName;
            set { _patEmergencyName = value; OnPropertyChanged(); }
        }

        public string PatEmergencyPhone
        {
            get => _patEmergencyPhone;
            set { _patEmergencyPhone = value; OnPropertyChanged(); }
        }

        public string PatNotes
        {
            get => _patNotes;
            set { _patNotes = value; OnPropertyChanged(); }
        }

        // ── Patient Picker Overlay Properties ──────────────────────────────────

        public bool IsPatientPickerVisible
        {
            get => _isPatientPickerVisible;
            set { _isPatientPickerVisible = value; OnPropertyChanged(); }
        }

        public string PickerSearchQuery
        {
            get => _pickerSearchQuery;
            set { _pickerSearchQuery = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PatientProfile> PickerPatients
        {
            get => _pickerPatients;
            set { _pickerPatients = value; OnPropertyChanged(); }
        }

        public PatientProfile? PickerSelectedPatient
        {
            get => _pickerSelectedPatient;
            set
            {
                _pickerSelectedPatient = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirmSaveToPatient));
            }
        }

        public string ImageTag
        {
            get => _imageTag;
            set { _imageTag = value; OnPropertyChanged(); }
        }

        public string ImageDescription
        {
            get => _imageDescription;
            set { _imageDescription = value; OnPropertyChanged(); }
        }

        public bool CanConfirmSaveToPatient => _pickerSelectedPatient != null;

        // ── Patient Images Display Properties ──────────────────────────────────

        public ObservableCollection<PatientImage> SelectedPatientImages
        {
            get => _selectedPatientImages;
            set { _selectedPatientImages = value; OnPropertyChanged(); }
        }

        public PatientImage? SelectedPatientImage
        {
            get => _selectedPatientImage;
            set
            {
                _selectedPatientImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedImage));
                if (value != null)
                {
                    EditImageTag = value.Tag;
                    EditImageDescription = value.Description;
                }
            }
        }

        public bool HasSelectedImage => _selectedPatientImage != null;

        public string EditImageTag
        {
            get => _editImageTag;
            set { _editImageTag = value; OnPropertyChanged(); }
        }

        public string EditImageDescription
        {
            get => _editImageDescription;
            set { _editImageDescription = value; OnPropertyChanged(); }
        }

        public ICommand NavigateCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand OpenImageCommand { get; }
        public ICommand ClearImageCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand FitToScreenCommand { get; }
        public ICommand NextFrameCommand { get; }
        public ICommand PreviousFrameCommand { get; }
        public ICommand FirstFrameCommand { get; }
        public ICommand LastFrameCommand { get; }
        public ICommand RunScanFilterCommand { get; }
        public ICommand SaveImageCommand { get; }
        public ICommand BrowseCsvCommand { get; }
        public ICommand BrowseMrdCommand { get; }
        public ICommand ClearGatingInputsCommand { get; }
        public ICommand RunCardiacGatingCommand { get; }
        public ICommand NewPatientCommand { get; }
        public ICommand SavePatientCommand { get; }
        public ICommand DeletePatientCommand { get; }
        public ICommand SearchPatientsCommand { get; }
        public ICommand ClearPatientFormCommand { get; }
        // Patient picker commands
        public ICommand ConfirmSaveToPatientCommand { get; }
        public ICommand CancelSaveToPatientCommand { get; }
        public ICommand SearchPickerPatientsCommand { get; }
        // Patient image commands
        public ICommand UpdateImageTagCommand { get; }
        public ICommand RemovePatientImageCommand { get; }
        public ICommand ViewPatientImageCommand { get; }

        public MainViewModel()
        {
            NavigateCommand = new RelayCommand(Navigate);
            SubmitCommand = new RelayCommand(Submit);
            OpenImageCommand = new RelayCommand(OpenImage);
            ClearImageCommand = new RelayCommand(ClearImage);
            ZoomInCommand = new RelayCommand(ZoomIn);
            ZoomOutCommand = new RelayCommand(ZoomOut);
            FitToScreenCommand = new RelayCommand(FitToScreen);
            NextFrameCommand = new RelayCommand(NextFrame, CanGoNextFrame);
            PreviousFrameCommand = new RelayCommand(PreviousFrame, CanGoPreviousFrame);
            FirstFrameCommand = new RelayCommand(FirstFrame, CanGoPreviousFrame);
            LastFrameCommand = new RelayCommand(LastFrame, CanGoNextFrame);
            RunScanFilterCommand = new RelayCommand(RunScanFilter, _ => !_isFilterRunning);
            SaveImageCommand = new RelayCommand(SaveImageToPatientFile, _ => CurrentImageSource != null);
            BrowseCsvCommand = new RelayCommand(BrowseCsv);
            BrowseMrdCommand = new RelayCommand(BrowseMrd);
            ClearGatingInputsCommand = new RelayCommand(ClearGatingInputs);
            RunCardiacGatingCommand = new RelayCommand(RunCardiacGating, _ => CanRunGating);
            NewPatientCommand = new RelayCommand(NewPatient);
            SavePatientCommand = new RelayCommand(SavePatient);
            DeletePatientCommand = new RelayCommand(DeletePatient, _ => HasSelectedPatient);
            SearchPatientsCommand = new RelayCommand(SearchPatients);
            ClearPatientFormCommand = new RelayCommand(ClearPatientForm);
            ConfirmSaveToPatientCommand = new RelayCommand(ConfirmSaveToPatient, _ => CanConfirmSaveToPatient);
            CancelSaveToPatientCommand = new RelayCommand(CancelSaveToPatient);
            SearchPickerPatientsCommand = new RelayCommand(SearchPickerPatients);
            UpdateImageTagCommand = new RelayCommand(UpdateImageTag, _ => HasSelectedImage && HasSelectedPatient);
            RemovePatientImageCommand = new RelayCommand(RemovePatientImage, _ => HasSelectedImage && HasSelectedPatient);
            ViewPatientImageCommand = new RelayCommand(ViewPatientImage, _ => HasSelectedImage && HasSelectedPatient);

            LoadAllPatients();
        }

        private void Navigate(object parameter)
        {
            CurrentPage = parameter?.ToString() ?? "Dashboard";
            StatusText = $"Navigated to {CurrentPage}";
        }

        private void Submit(object parameter)
        {
            if (!string.IsNullOrWhiteSpace(UserInput))
            {
                StatusMessage = $"You submitted: {UserInput}";
                StatusText = "Data submitted successfully";
            }
            else
            {
                StatusMessage = "Please enter some text";
                StatusText = "Submission failed";
            }
        }

        private void OpenImage(object parameter)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select DICOM Image",
                Filter = "DICOM Files|*.dcm;*.dicom",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Reset filtered series mode when opening a single file
                    _isFilteredSeriesMode = false;
                    _filteredDicomFiles.Clear();
                    FilterResultSummary = "";

                    _currentImagePath = openFileDialog.FileName;

                    var dicomFile = DicomFile.Open(_currentImagePath);
                    _currentDicomImage = new DicomImage(dicomFile.Dataset);

                    TotalFrames = _currentDicomImage.NumberOfFrames;
                    CurrentFrameIndex = 0;

                    LoadCurrentFrame();
                    ZoomLevel = 1.0;

                    StatusText = $"Loaded: {Path.GetFileName(_currentImagePath)} ({TotalFrames} frame{(TotalFrames > 1 ? "s" : "")})";
                }
                catch (System.Exception ex)
                {
                    StatusText = $"Error loading DICOM: {ex.Message}";
                    ImageInfo = "Failed to load DICOM";
                }
            }
        }

        private void LoadCurrentFrame()
        {
            if (_isFilteredSeriesMode && _filteredDicomFiles.Count > 0)
            {
                LoadFilteredFrame();
                return;
            }

            if (_currentDicomImage == null) return;

            try
            {
                var image = _currentDicomImage.RenderImage(CurrentFrameIndex);
                RenderImageToSource(image);
            }
            catch (System.Exception ex)
            {
                StatusText = $"Error rendering frame: {ex.Message}";
            }
        }

        private void LoadFilteredFrame()
        {
            if (CurrentFrameIndex < 0 || CurrentFrameIndex >= _filteredDicomFiles.Count) return;

            try
            {
                string filePath = _filteredDicomFiles[CurrentFrameIndex];
                var dicomFile = DicomFile.Open(filePath);
                var dicomImage = new DicomImage(dicomFile.Dataset);
                var image = dicomImage.RenderImage(0);
                RenderImageToSource(image);
                _currentImagePath = filePath;
            }
            catch (System.Exception ex)
            {
                StatusText = $"Error rendering filtered frame: {ex.Message}";
            }
        }

        private void RenderImageToSource(FellowOakDicom.Imaging.IImage image)
        {
            int width = image.Width;
            int height = image.Height;

            var bitmap = new WriteableBitmap(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);

            var pixelData = image.AsBytes();

            bitmap.Lock();
            try
            {
                unsafe
                {
                    int stride = bitmap.BackBufferStride;
                    byte* pBackBuffer = (byte*)bitmap.BackBuffer;

                    for (int i = 0; i < pixelData.Length && i < stride * height; i++)
                    {
                        pBackBuffer[i] = pixelData[i];
                    }
                }

                bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
            }
            finally
            {
                bitmap.Unlock();
            }

            CurrentImageSource = bitmap;
        }

        private void ClearImage(object parameter)
        {
            CurrentImageSource = null;
            _currentImagePath = "";
            _currentDicomImage = null;
            CurrentFrameIndex = 0;
            TotalFrames = 0;
            ZoomLevel = 1.0;
            ImageInfo = "No image loaded";
            ImageDimensions = "";
            StatusText = "Image cleared";

            // Reset filtered series mode
            _isFilteredSeriesMode = false;
            _filteredDicomFiles.Clear();
            FilterResultSummary = "";
        }

        private void ZoomIn(object parameter)
        {
            if (CurrentImageSource != null)
            {
                ZoomLevel = System.Math.Min(ZoomLevel * 1.2, 10.0);
                StatusText = $"Zoom: {ZoomPercentage:F0}%";
            }
        }

        private void ZoomOut(object parameter)
        {
            if (CurrentImageSource != null)
            {
                ZoomLevel = System.Math.Max(ZoomLevel / 1.2, 0.1);
                StatusText = $"Zoom: {ZoomPercentage:F0}%";
            }
        }

        private void FitToScreen(object parameter)
        {
            if (CurrentImageSource != null)
            {
                ZoomLevel = 1.0;
                StatusText = "Fit to screen";
            }
        }

        private bool CanGoNextFrame(object parameter)
        {
            if (_isFilteredSeriesMode)
                return _filteredDicomFiles.Count > 0 && CurrentFrameIndex < _filteredDicomFiles.Count - 1;
            return _currentDicomImage != null && CurrentFrameIndex < TotalFrames - 1;
        }

        private void NextFrame(object parameter)
        {
            if (CanGoNextFrame(null))
            {
                CurrentFrameIndex++;
                LoadCurrentFrame();
            }
        }

        private bool CanGoPreviousFrame(object parameter)
        {
            if (_isFilteredSeriesMode)
                return _filteredDicomFiles.Count > 0 && CurrentFrameIndex > 0;
            return _currentDicomImage != null && CurrentFrameIndex > 0;
        }

        private void PreviousFrame(object parameter)
        {
            if (CanGoPreviousFrame(null))
            {
                CurrentFrameIndex--;
                LoadCurrentFrame();
            }
        }

        private void FirstFrame(object parameter)
        {
            if ((_isFilteredSeriesMode ? _filteredDicomFiles.Count > 0 : _currentDicomImage != null) && CurrentFrameIndex != 0)
            {
                CurrentFrameIndex = 0;
                LoadCurrentFrame();
            }
        }

        private void LastFrame(object parameter)
        {
            int lastIndex = _isFilteredSeriesMode ? _filteredDicomFiles.Count - 1 : TotalFrames - 1;
            if (lastIndex >= 0 && CurrentFrameIndex != lastIndex)
            {
                CurrentFrameIndex = lastIndex;
                LoadCurrentFrame();
            }
        }

        private async void RunScanFilter(object parameter)
        {
            // Step 1: Select cardiogram CSV
            var csvDialog = new OpenFileDialog
            {
                Title = "Select Cardiogram CSV File",
                Filter = "CSV Files|*.csv|All Files|*.*",
                FilterIndex = 1
            };

            if (csvDialog.ShowDialog() != true) return;
            string csvPath = csvDialog.FileName;

            // Step 2: Select MRD scan files (multi-select)
            var mrdDialog = new OpenFileDialog
            {
                Title = "Select MRD Scan File(s)",
                Filter = "MRD Files|*.mrd|All Files|*.*",
                FilterIndex = 1,
                Multiselect = true
            };

            if (mrdDialog.ShowDialog() != true) return;
            string[] mrdPaths = mrdDialog.FileNames;

            if (mrdPaths.Length == 0) return;

            // Step 3: Run the C# gating pipeline
            IsFilterRunning = true;
            FilterResultSummary = "";
            StatusText = "Running scan filter (cardiac gating)...";

            string outputDir = Path.Combine(
                Path.GetTempPath(),
                "BioMetrix_Filtered_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                var result = await _scanFilterService.RunFilterAsync(
                    csvPath, mrdPaths, outputDir);

                if (result.Success)
                {
                    FilterResultSummary =
                        $"Total lines: {result.TotalLines}, " +
                        $"Clean: {result.CleanBaseLines}, " +
                        $"Replaced: {result.ReplacedLines}, " +
                        $"Unfixable: {result.UnfixableLines}";
                    StatusText = $"Scan filter complete. {result.ReplacedLines} lines replaced via gating.";
                    LoadFilteredDicomFolder(result.OutputDir);
                }
                else
                {
                    StatusText = $"Scan filter failed: {result.Error}";
                    FilterResultSummary = "";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Scan filter error: {ex.Message}";
            }
            finally
            {
                IsFilterRunning = false;
            }
        }

        private void LoadFilteredDicomFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            var files = Directory.GetFiles(folderPath, "*.dcm")
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                StatusText = "No DICOM files found in filtered output.";
                return;
            }

            _filteredDicomFiles = files;
            _isFilteredSeriesMode = true;
            _currentDicomImage = null;

            TotalFrames = files.Count;
            CurrentFrameIndex = 0;
            ZoomLevel = 1.0;

            LoadCurrentFrame();
        }

        private void SaveImageToPatientFile(object parameter)
        {
            if (CurrentImageSource == null)
            {
                StatusText = "No image loaded to save.";
                return;
            }

            // Determine source type automatically
            if (_isFilteredSeriesMode)
                _imageSourceType = "Scan Filter";
            else if (!string.IsNullOrEmpty(_gatingResultSummary))
                _imageSourceType = "Cardiac Gating";
            else
                _imageSourceType = "Manual Import";

            // Load all patients into the picker and show the overlay
            var allPatients = _patientService.LoadAllPatients();
            PickerPatients = new ObservableCollection<PatientProfile>(allPatients);
            PickerSelectedPatient = null;
            PickerSearchQuery = "";
            ImageTag = "";
            ImageDescription = "";
            IsPatientPickerVisible = true;
            StatusText = "Select a patient to save this image to.";
        }

        private void ConfirmSaveToPatient(object parameter)
        {
            if (PickerSelectedPatient == null || CurrentImageSource == null) return;

            try
            {
                // Ensure we have a file on disk to copy into managed storage.
                // If we have the original DICOM, use it; otherwise save a temp PNG.
                string fileToSave = _currentImagePath;
                bool usingTempFile = false;

                if (string.IsNullOrEmpty(fileToSave) || !File.Exists(fileToSave))
                {
                    // No source file on disk — render current image to a temp PNG
                    string tempPath = Path.Combine(Path.GetTempPath(),
                        $"BioMetrix_save_{Guid.NewGuid():N}.png");
                    SaveBitmapSourceAsPng(CurrentImageSource, tempPath);
                    fileToSave = tempPath;
                    usingTempFile = true;
                }

                var savedImage = _patientService.SaveImageForPatient(
                    PickerSelectedPatient.PatientId,
                    fileToSave,
                    ImageTag,
                    ImageDescription,
                    _imageSourceType);

                if (usingTempFile && File.Exists(fileToSave))
                    File.Delete(fileToSave);

                IsPatientPickerVisible = false;
                StatusText = $"Image saved to patient '{PickerSelectedPatient.FullName}' with tag '{savedImage.Tag}'.";

                // Refresh patient list so image counts are up to date
                LoadAllPatients();

                // If the saved patient is currently selected in the Patients tab, refresh images
                if (_selectedPatient?.PatientId == PickerSelectedPatient.PatientId)
                    RefreshSelectedPatientImages();
            }
            catch (Exception ex)
            {
                StatusText = $"Error saving image to patient: {ex.Message}";
            }
        }

        private void CancelSaveToPatient(object parameter)
        {
            IsPatientPickerVisible = false;
            StatusText = "Save to patient cancelled.";
        }

        private void SearchPickerPatients(object parameter)
        {
            var results = _patientService.SearchPatients(PickerSearchQuery);
            PickerPatients = new ObservableCollection<PatientProfile>(results);
        }

        // ── Patient Image Management Methods ──────────────────────────────────

        private void RefreshSelectedPatientImages()
        {
            if (_selectedPatient == null)
            {
                SelectedPatientImages = new ObservableCollection<PatientImage>();
                return;
            }

            // Reload from storage to get fresh data
            var fresh = _patientService.LoadPatient(_selectedPatient.PatientId);
            if (fresh != null)
                SelectedPatientImages = new ObservableCollection<PatientImage>(fresh.AssociatedImages);
            else
                SelectedPatientImages = new ObservableCollection<PatientImage>();
        }

        private void UpdateImageTag(object parameter)
        {
            if (_selectedPatient == null || _selectedPatientImage == null) return;

            try
            {
                _patientService.UpdateImageMetadata(
                    _selectedPatient.PatientId,
                    _selectedPatientImage.ImageId,
                    EditImageTag,
                    EditImageDescription);

                PatientStatusMessage = $"Image tag updated to '{EditImageTag}'.";
                RefreshSelectedPatientImages();
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error updating image tag: {ex.Message}";
            }
        }

        private void RemovePatientImage(object parameter)
        {
            if (_selectedPatient == null || _selectedPatientImage == null) return;

            var result = MessageBox.Show(
                $"Remove image '{_selectedPatientImage.DisplayLabel}' from this patient?\n\n" +
                "The image file will be permanently deleted.",
                "Confirm Remove Image",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _patientService.RemoveImageFromPatient(
                    _selectedPatient.PatientId,
                    _selectedPatientImage.ImageId);

                PatientStatusMessage = "Image removed.";
                SelectedPatientImage = null;
                EditImageTag = "";
                EditImageDescription = "";
                RefreshSelectedPatientImages();
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error removing image: {ex.Message}";
            }
        }

        private void ViewPatientImage(object parameter)
        {
            if (_selectedPatient == null || _selectedPatientImage == null) return;

            try
            {
                string fullPath = _patientService.GetImageFullPath(
                    _selectedPatient.PatientId,
                    _selectedPatientImage.FileName);

                if (!File.Exists(fullPath))
                {
                    PatientStatusMessage = "Image file not found on disk.";
                    return;
                }

                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                if (ext == ".dcm" || ext == ".dicom")
                {
                    // Load as DICOM into the Image Viewer
                    _isFilteredSeriesMode = false;
                    _filteredDicomFiles.Clear();
                    FilterResultSummary = "";
                    _currentImagePath = fullPath;

                    var dicomFile = DicomFile.Open(fullPath);
                    _currentDicomImage = new DicomImage(dicomFile.Dataset);
                    TotalFrames = _currentDicomImage.NumberOfFrames;
                    CurrentFrameIndex = 0;
                    LoadCurrentFrame();
                    ZoomLevel = 1.0;
                }
                else
                {
                    // Load as bitmap
                    _isFilteredSeriesMode = false;
                    _filteredDicomFiles.Clear();
                    _currentDicomImage = null;
                    _currentImagePath = fullPath;
                    TotalFrames = 1;
                    CurrentFrameIndex = 0;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    CurrentImageSource = bitmap;
                    ZoomLevel = 1.0;
                }

                StatusText = $"Viewing patient image: {_selectedPatientImage.DisplayLabel}";
                PatientStatusMessage = "Image loaded into viewer. Switch to Image Viewer tab to see it.";
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error loading image: {ex.Message}";
            }
        }

        private static void SaveBitmapSourceAsPng(BitmapSource source, string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(outputPath);
            encoder.Save(stream);
        }

        private static void SaveBitmapSourceAsDicom(BitmapSource source, string outputPath)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;

            // Convert to grayscale 16-bit pixel data
            var grayscale = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Gray16, null, 0);
            int stride = width * 2; // 16 bits per pixel
            byte[] pixelBytes = new byte[height * stride];
            grayscale.CopyPixels(pixelBytes, stride, 0);

            var dataset = new DicomDataset();
            dataset.AddOrUpdate(DicomTag.MediaStorageSOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
            dataset.AddOrUpdate(DicomTag.MediaStorageSOPInstanceUID, DicomUID.Generate().UID);
            dataset.AddOrUpdate(DicomTag.TransferSyntaxUID, "1.2.840.10008.1.2.1");
            dataset.AddOrUpdate(DicomTag.PatientName, "PATIENT");
            dataset.AddOrUpdate(DicomTag.PatientID, "PAT_001");
            dataset.AddOrUpdate(DicomTag.StudyDescription, "Saved from Image Viewer");
            dataset.AddOrUpdate(DicomTag.SeriesDescription, "Patient Image");
            dataset.AddOrUpdate(DicomTag.Modality, "OT");
            dataset.AddOrUpdate(DicomTag.StudyInstanceUID, DicomUID.Generate().UID);
            dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);
            dataset.AddOrUpdate(DicomTag.SOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
            dataset.AddOrUpdate(DicomTag.SOPInstanceUID, DicomUID.Generate().UID);
            dataset.AddOrUpdate(DicomTag.StudyDate, DateTime.UtcNow.ToString("yyyyMMdd"));
            dataset.AddOrUpdate(DicomTag.StudyTime, DateTime.UtcNow.ToString("HHmmss"));
            dataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)1);
            dataset.AddOrUpdate(DicomTag.PhotometricInterpretation, "MONOCHROME2");
            dataset.AddOrUpdate(DicomTag.Rows, (ushort)height);
            dataset.AddOrUpdate(DicomTag.Columns, (ushort)width);
            dataset.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
            dataset.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
            dataset.AddOrUpdate(DicomTag.HighBit, (ushort)15);
            dataset.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
            dataset.AddOrUpdate(new DicomOtherWord(DicomTag.PixelData,
                new FellowOakDicom.IO.Buffer.MemoryByteBuffer(pixelBytes)));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            var file = new DicomFile(dataset);
            file.Save(outputPath);
        }

        private void BrowseCsv(object parameter)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Heart Rate / Cardiogram CSV",
                Filter = "CSV Files|*.csv|All Files|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedCsvPath = dialog.FileName;
                StatusText = $"CSV selected: {SelectedCsvFileName}";
            }
        }

        private void BrowseMrd(object parameter)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select MRD Scan File",
                Filter = "MRD Files|*.mrd|All Files|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedMrdPath = dialog.FileName;
                StatusText = $"MRD selected: {SelectedMrdFileName}";
            }
        }

        private void ClearGatingInputs(object parameter)
        {
            SelectedCsvPath = "";
            SelectedMrdPath = "";
            GatingResultSummary = "";
            StatusText = "Cardiac gating inputs cleared";
        }

        private async void RunCardiacGating(object parameter)
        {
            if (string.IsNullOrEmpty(_selectedCsvPath) || string.IsNullOrEmpty(_selectedMrdPath))
            {
                StatusText = "Please select both a CSV and MRD file first.";
                return;
            }

            IsGatingRunning = true;
            GatingResultSummary = "";
            StatusText = "Initializing Python runtime...";

            string outputDir = Path.Combine(
                Path.GetTempPath(),
                "BioMetrix_Gated_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                _cardiacGatingService.EnsurePythonInitialized();
                StatusText = "Running cardiac gating pipeline...";

                var result = await _cardiacGatingService.RunGatingAsync(
                    _selectedCsvPath, _selectedMrdPath, outputDir);

                if (result.Success)
                {
                    GatingResultSummary = $"Accepted: {result.AcceptedFrames}, Rejected: {result.RejectedFrames}, Stable cycles: {result.StableCycles}, Offset: {result.AlignmentOffsetMs:F2} ms";
                    StatusText = $"Cardiac gating complete. {result.AcceptedFrames} frames accepted.";
                    LoadFilteredDicomFolder(result.OutputDir);
                }
                else
                {
                    // Show short message in status bar, full error + diagnostics in summary
                    string firstLine = result.Error.Split('\n')[0];
                    StatusText = $"Cardiac gating failed: {firstLine}";
                    GatingResultSummary = result.Error;
                    System.Diagnostics.Debug.WriteLine($"[CardiacGating] Error:\n{result.Error}");
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Cardiac gating error: {ex.Message}";
            }
            finally
            {
                IsGatingRunning = false;
            }
        }

        // ── Patient Management Methods ─────────────────────────────────────────

        private void LoadAllPatients()
        {
            try
            {
                var patients = _patientService.LoadAllPatients();
                Patients = new ObservableCollection<PatientProfile>(patients);
                PatientStatusMessage = $"{patients.Count} patient(s) loaded.";
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error loading patients: {ex.Message}";
            }
        }

        private void NewPatient(object parameter)
        {
            SelectedPatient = null;
            IsEditingPatient = false;
            ClearPatientFormFields();
            PatientStatusMessage = "Enter new patient details and click Save.";
        }

        private void SavePatient(object parameter)
        {
            var profile = IsEditingPatient && _selectedPatient != null
                ? _selectedPatient
                : new PatientProfile();

            profile.FirstName = PatFirstName.Trim();
            profile.LastName = PatLastName.Trim();
            profile.DateOfBirth = PatDateOfBirth;
            profile.Gender = PatGender.Trim();
            profile.MedicalRecordNumber = PatMrn.Trim();
            profile.PhoneNumber = PatPhone.Trim();
            profile.Email = PatEmail.Trim();
            profile.Address = PatAddress.Trim();
            profile.EmergencyContactName = PatEmergencyName.Trim();
            profile.EmergencyContactPhone = PatEmergencyPhone.Trim();
            profile.MedicalNotes = PatNotes.Trim();

            if (!IsEditingPatient)
            {
                profile.CreatedDate = DateTime.UtcNow;
                profile.CreatedByProviderId =
                    AuthenticationService.CurrentProvider?.ProviderId ?? "UNKNOWN";
            }

            string? error = PatientService.ValidateProfile(profile);
            if (error != null)
            {
                PatientStatusMessage = $"Validation error: {error}";
                return;
            }

            try
            {
                _patientService.SavePatient(profile);
                PatientStatusMessage = IsEditingPatient
                    ? $"Patient '{profile.FullName}' updated successfully."
                    : $"Patient '{profile.FullName}' created successfully.";
                LoadAllPatients();

                // Select the saved patient in the list
                SelectedPatient = Patients.FirstOrDefault(p => p.PatientId == profile.PatientId);
                IsEditingPatient = true;
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error saving patient: {ex.Message}";
            }
        }

        private void DeletePatient(object parameter)
        {
            if (_selectedPatient == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete patient '{_selectedPatient.FullName}'?\n\n" +
                "This action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                string name = _selectedPatient.FullName;
                _patientService.DeletePatient(_selectedPatient.PatientId);
                PatientStatusMessage = $"Patient '{name}' deleted.";
                ClearPatientFormFields();
                SelectedPatient = null;
                IsEditingPatient = false;
                LoadAllPatients();
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Error deleting patient: {ex.Message}";
            }
        }

        private void SearchPatients(object parameter)
        {
            try
            {
                var results = _patientService.SearchPatients(PatientSearchQuery);
                Patients = new ObservableCollection<PatientProfile>(results);
                PatientStatusMessage = string.IsNullOrWhiteSpace(PatientSearchQuery)
                    ? $"{results.Count} patient(s) found."
                    : $"{results.Count} result(s) for \"{PatientSearchQuery}\".";
            }
            catch (Exception ex)
            {
                PatientStatusMessage = $"Search error: {ex.Message}";
            }
        }

        private void ClearPatientForm(object parameter)
        {
            ClearPatientFormFields();
            SelectedPatient = null;
            IsEditingPatient = false;
            SelectedPatientImages = new ObservableCollection<PatientImage>();
            SelectedPatientImage = null;
            EditImageTag = "";
            EditImageDescription = "";
            PatientStatusMessage = "Form cleared.";
        }

        private void LoadPatientIntoForm(PatientProfile patient)
        {
            PatFirstName = patient.FirstName;
            PatLastName = patient.LastName;
            PatDateOfBirth = patient.DateOfBirth;
            PatGender = patient.Gender;
            PatMrn = patient.MedicalRecordNumber;
            PatPhone = patient.PhoneNumber;
            PatEmail = patient.Email;
            PatAddress = patient.Address;
            PatEmergencyName = patient.EmergencyContactName;
            PatEmergencyPhone = patient.EmergencyContactPhone;
            PatNotes = patient.MedicalNotes;
            IsEditingPatient = true;
            PatientStatusMessage = $"Editing: {patient.FullName}";
            RefreshSelectedPatientImages();
        }

        private void ClearPatientFormFields()
        {
            PatFirstName = "";
            PatLastName = "";
            PatDateOfBirth = DateTime.Today;
            PatGender = "";
            PatMrn = "";
            PatPhone = "";
            PatEmail = "";
            PatAddress = "";
            PatEmergencyName = "";
            PatEmergencyPhone = "";
            PatNotes = "";
        }

        private void UpdateImageInfo()
        {
            if (CurrentImageSource != null)
            {
                ImageInfo = $"File: {Path.GetFileName(_currentImagePath)}";
                ImageDimensions = $"{CurrentImageSource.PixelWidth} x {CurrentImageSource.PixelHeight} pixels";
            }
            else
            {
                ImageInfo = "No image loaded";
                ImageDimensions = "";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
