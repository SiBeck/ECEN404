using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using _403DesktopApp.Services;

namespace _403DesktopApp
{
    public class ForgotPasswordViewModel : INotifyPropertyChanged
    {
        private readonly AuthenticationService _authService;
        private string _providerId = "";
        private string _email = "";
        private string _statusMessage = "";

        public string ProviderId
        {
            get => _providerId;
            set { _providerId = value; OnPropertyChanged(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ICommand VerifyCommand { get; }
        public ICommand CancelCommand { get; }

        public ForgotPasswordViewModel(string initialProviderId, AuthenticationService authService = null)
        {
            _authService = authService ?? new AuthenticationService();
            ProviderId = initialProviderId ?? "";
            VerifyCommand = new RelayCommand(_ => { /* handled by window code-behind */ });
            CancelCommand = new RelayCommand(_ => { /* handled by window */ });
        }

        // Called by window code-behind. Returns true if verification succeeds.
        public bool Verify()
        {
            StatusMessage = "";

            if (string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(Email))
            {
                StatusMessage = "Please enter both Provider ID and registered email.";
                return false;
            }

            var provider = _authService.GetProvider(ProviderId);
            if (provider == null)
            {
                StatusMessage = "Provider ID not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(provider.Email))
            {
                StatusMessage = "No email on file for that provider.";
                return false;
            }

            if (!string.Equals(provider.Email.Trim(), Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Email does not match our records.";
                return false;
            }

            // In production send a reset token/email. Here we simulate success.
            StatusMessage = "Verification successful. Reset instructions were sent to the registered email.";
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}