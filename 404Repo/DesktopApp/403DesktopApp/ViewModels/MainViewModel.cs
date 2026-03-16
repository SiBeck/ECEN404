using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;
using FellowOakDicom;
using FellowOakDicom.Imaging;

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
            if (_currentDicomImage == null) return;

            try
            {
                var image = _currentDicomImage.RenderImage(CurrentFrameIndex);

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
            catch (System.Exception ex)
            {
                StatusText = $"Error rendering frame: {ex.Message}";
            }
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
            if (_currentDicomImage != null && CurrentFrameIndex != 0)
            {
                CurrentFrameIndex = 0;
                LoadCurrentFrame();
            }
        }

        private void LastFrame(object parameter)
        {
            if (_currentDicomImage != null && CurrentFrameIndex != TotalFrames - 1)
            {
                CurrentFrameIndex = TotalFrames - 1;
                LoadCurrentFrame();
            }
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