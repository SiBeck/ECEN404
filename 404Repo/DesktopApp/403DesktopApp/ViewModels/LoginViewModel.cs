using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using _403DesktopApp.Services;

namespace _403DesktopApp
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;
        private readonly AuthenticationService _authService;
        private string _providerId = "";
        private string _password = "";
        private string _errorMessage = "";

        public string ProviderId
        {
            get => _providerId;
            set { _providerId = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand LoginCommand { get; }
        public ICommand CancelCommand { get; }

        public LoginViewModel(Window window)
        {
            _window = window;
            _authService = new AuthenticationService();
            LoginCommand = new RelayCommand(Login);
            CancelCommand = new RelayCommand(Cancel);
        }

        private void Login(object parameter)
        {
            ErrorMessage = "";

            if (string.IsNullOrWhiteSpace(ProviderId))
            {
                ErrorMessage = "Please enter your Provider ID";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Please enter your password";
                return;
            }

            if (_authService.AuthenticateProvider(ProviderId, Password))
            {
                _window.DialogResult = true;
                _window.Close();
            }
            else
            {
                ErrorMessage = "Invalid Provider ID or password. Please try again.";
                Password = "";
            }
        }

        private void Cancel(object parameter)
        {
            _window.DialogResult = false;
            _window.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}